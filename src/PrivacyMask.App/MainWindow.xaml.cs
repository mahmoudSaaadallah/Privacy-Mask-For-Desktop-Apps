using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.App.Windows;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.App;

public partial class MainWindow : Window
{
    public static readonly Array MaskColors = Enum.GetValues<MaskColorOption>();

    private bool _allowClose;
    private bool _isSaving;
    private bool _previewQueued;
    private readonly DesktopApplicationCatalog _applicationCatalog;

    public MainWindow(SettingsViewModel viewModel, DesktopApplicationCatalog applicationCatalog)
    {
        _applicationCatalog = applicationCatalog;
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
        ReplaceViewModel(viewModel);
    }

    public event Func<AppSettings, Task>? SaveRequested;

    public event Action<AppSettings>? PreviewRequested;

    public event Action? DiscardRequested;

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public IReadOnlyList<MaskStyleOption> MaskStylesSource => MaskStyleOption.All;

    public Array MaskColorsSource => MaskColors;

    public IReadOnlyList<ActivationModeOption> ActivationModesSource => ActivationModeOption.All;

    public void ReplaceViewModel(SettingsViewModel viewModel)
    {
        if (DataContext is SettingsViewModel previous)
        {
            viewModel.ApplicationSearch = previous.ApplicationSearch;
            var selected = viewModel.AppProfiles.FirstOrDefault(profile =>
                profile.ProfileId == previous.SelectedApplication?.ProfileId);
            if (selected is not null) viewModel.OpenApplication(selected);
            viewModel.SelectedPage = previous.SelectedPage;
            previous.SettingsEdited -= QueuePreview;
            previous.Dispose();
        }

        DataContext = viewModel;
        viewModel.SettingsEdited += QueuePreview;
    }

    private void QueuePreview()
    {
        if (!IsLoaded || _isSaving || _previewQueued || _allowClose) return;
        _previewQueued = true;
        var editedSession = ViewModel;
        Dispatcher.BeginInvoke(() =>
        {
            _previewQueued = false;
            if (!ReferenceEquals(editedSession, DataContext) || !IsVisible || _isSaving) return;
            var preview = editedSession.ToModel();
            if (!HasBindingErrors(this) && AppSettingsValidator.Validate(preview).Count == 0)
            {
                PreviewRequested?.Invoke(preview);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.SettingsEdited -= QueuePreview;
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DesktopPage page)
        {
            return;
        }

        if (!CommitFocusedEditor() || !CommitGridEditors(this) || HasBindingErrors(this))
        {
            System.Windows.MessageBox.Show(this,
                "Correct the highlighted value before changing pages. Your edits are retained.",
                "Check this value", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (page == DesktopPage.Applications) ViewModel.ShowApplicationList();
        else ViewModel.SelectedPage = page;
    }

    private void ManageApplication_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppProfileViewModel profile)
        {
            ViewModel.OpenApplication(profile);
        }
    }

    private void BackToApplications_Click(object sender, RoutedEventArgs e)
    {
        Navigate_Click(new System.Windows.Controls.Button { Tag = DesktopPage.Applications }, e);
    }

    private static bool CommitGridEditors(DependencyObject element)
    {
        if (element is System.Windows.Controls.DataGrid grid
            && (!grid.CommitEdit(DataGridEditingUnit.Cell, true)
                || !grid.CommitEdit(DataGridEditingUnit.Row, true)))
        {
            return false;
        }

        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); index++)
        {
            if (!CommitGridEditors(System.Windows.Media.VisualTreeHelper.GetChild(element, index)))
            {
                return false;
            }
        }

        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SaveRequested is null || _isSaving)
        {
            return;
        }

        if (!CommitFocusedEditor() || !CommitGridEditors(this) || HasBindingErrors(this))
        {
            System.Windows.MessageBox.Show(
                this,
                "One or more fields contain text that cannot be converted to a number. Correct the highlighted value, then save again.",
                "Check these settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var settings = ViewModel.ToModel();
        var validationErrors = AppSettingsValidator.Validate(settings);
        if (validationErrors.Count > 0)
        {
            ShowValidationErrors(validationErrors);
            return;
        }

        _isSaving = true;
        ViewModel.IsSaving = true;
        IsEnabled = false;
        try
        {
            await SaveRequested.Invoke(settings);
            ViewModel.SetSavedBaseline(ViewModel.ToModel());
        }
        catch (Exception exception)
        {
            ViewModel.SaveError = exception.Message;
            System.Windows.MessageBox.Show(
                this,
                $"PrivacyMask could not save these changes.\n\n{exception.Message}",
                "Settings were not saved",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            _isSaving = false;
            ViewModel.IsSaving = false;
        }
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        DiscardRequested?.Invoke();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppProfileViewModel profile)
        {
            profile.ApplySelectedPreset();
            QueuePreview();
        }
    }

    private void MaskIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || sender is not Slider)
        {
            return;
        }

        QueuePreview();
    }

    private void HoverRevealDimension_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.TextBox)
        {
            return;
        }

        QueuePreview();
    }

    private void MaskColorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.ComboBox)
        {
            return;
        }

        QueuePreview();
    }

    private void MaskStyleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.ComboBox)
        {
            return;
        }

        QueuePreview();
    }

    private void NumericTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this,
        };

        aboutWindow.ShowDialog();
    }

    private void ConfigurePanicPin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PanicPinSetupWindow(ViewModel.PanicPin)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        if (dialog.ShowDialog() != true || dialog.CreatedCredential is null)
        {
            return;
        }

        ViewModel.SetPanicPin(dialog.CreatedCredential);
    }

    private void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AddApplicationWindow(_applicationCatalog, GetProtectedProcessNames())
        {
            Owner = this,
        };
        if (picker.ShowDialog() != true || picker.SelectedApplication is null)
        {
            return;
        }

        var profile = CustomAppProfileFactory.Create(
            picker.ProfileDisplayName,
            picker.SelectedApplication.ProcessName,
            executablePath: picker.SelectedApplication.ExecutablePath);
        var profileViewModel = new AppProfileViewModel(profile);
        ViewModel.AppProfiles.Add(profileViewModel);
        ViewModel.OpenApplication(profileViewModel);
        QueuePreview();
    }

    private void RelinkApplication_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppProfileViewModel { IsCustom: true } profile)
        {
            return;
        }

        var picker = new AddApplicationWindow(
            _applicationCatalog,
            GetProtectedProcessNames(profile),
            profile.DisplayName)
        {
            Owner = this,
        };
        if (picker.ShowDialog() != true || picker.SelectedApplication is null)
        {
            return;
        }

        profile.Relink(
            picker.SelectedApplication.ProcessName,
            picker.SelectedApplication.ExecutablePath);
        QueuePreview();
    }

    private void RemoveApplication_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppProfileViewModel { IsCustom: true } profile)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"Remove {profile.DisplayName} from PrivacyMask?\n\nThe application itself will not be changed. This removal is kept only after you save settings.",
            "Remove application profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        ViewModel.AppProfiles.Remove(profile);
        QueuePreview();
    }

    private void ProfileEnabled_Click(object sender, RoutedEventArgs e)
    {
        QueuePreview();
    }

    private IReadOnlySet<string> GetProtectedProcessNames(AppProfileViewModel? excludedProfile = null)
    {
        return ViewModel.AppProfiles
            .Where(profile => !ReferenceEquals(profile, excludedProfile))
            .SelectMany(profile => profile.WindowMatchers)
            .SelectMany(matcher => matcher.ProcessNames)
            .Where(processName => !string.IsNullOrWhiteSpace(processName))
            .Select(processName => processName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            DiscardAndHide();
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (InformationHelp.CloseIfOpen(Keyboard.FocusedElement))
            {
                e.Handled = true;
                return;
            }
            DiscardAndHide();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void DiscardAndHide()
    {
        DiscardRequested?.Invoke();
        Hide();
    }

    private static bool CommitFocusedEditor()
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox textBox)
        {
            textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            return !Validation.GetHasError(textBox);
        }

        return true;
    }

    private static bool HasBindingErrors(DependencyObject element)
    {
        if (Validation.GetHasError(element))
        {
            return true;
        }

        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); index++)
        {
            if (HasBindingErrors(System.Windows.Media.VisualTreeHelper.GetChild(element, index)))
            {
                return true;
            }
        }

        return false;
    }

    private void ShowValidationErrors(IReadOnlyList<SettingsValidationError> errors)
    {
        const int maximumVisibleErrors = 8;
        var lines = errors
            .Take(maximumVisibleErrors)
            .Select(error => $"• {error.Location}: {error.Message}")
            .ToList();

        if (errors.Count > maximumVisibleErrors)
        {
            lines.Add($"• And {errors.Count - maximumVisibleErrors} more issue(s).");
        }

        System.Windows.MessageBox.Show(
            this,
            $"Correct the following values before saving:\n\n{string.Join("\n", lines)}",
            "Check these settings",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
