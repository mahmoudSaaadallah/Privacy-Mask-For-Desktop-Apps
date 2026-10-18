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
    private bool _isShuttingDown;
    private bool _isUpdateSuspended;
    private TaskCompletionSource? _saveFinished;
    private TaskCompletionSource? _runtimeActionFinished;
    private TaskCompletionSource? _updateCheckFinished;
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
    public event Action? GlobalProtectionRequested;
    public event Func<Task>? GlobalPanicRequested;
    public event Func<Task>? CheckForUpdatesRequested;
    public event Func<Task>? DownloadUpdateRequested;
    public event Func<Task>? DiscardUpdateRequested;
    public event Action? CancelUpdateDownloadRequested;

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e) =>
        await RunDownloadActionAsync(DownloadUpdateRequested);

    private async void DiscardUpdate_Click(object sender, RoutedEventArgs e) =>
        await RunDownloadActionAsync(DiscardUpdateRequested);

    private void CancelUpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (!_isShuttingDown && !_isUpdateSuspended) CancelUpdateDownloadRequested?.Invoke();
    }

    private async Task RunDownloadActionAsync(Func<Task>? action)
    {
        if (_isShuttingDown || _isUpdateSuspended || action is null || _updateCheckFinished?.Task.IsCompleted == false) return;
        _updateCheckFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await action(); }
        catch (Exception)
        {
            ViewModel.Updates.FinishDownload("The download could not be completed. No update was installed.", cleanupIncomplete: true);
        }
        finally { _updateCheckFinished.SetResult(); }
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_isShuttingDown || _isUpdateSuspended || _updateCheckFinished?.Task.IsCompleted == false
            || CheckForUpdatesRequested is null) return;
        _updateCheckFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await CheckForUpdatesRequested.Invoke(); }
        catch (Exception)
        {
            ViewModel.Updates.Complete(new(PrivacyMask.Updates.UpdateCheckStatus.UnableToCheck),
                ViewModel.UpdateChannel, DateTimeOffset.UtcNow);
        }
        finally { _updateCheckFinished.SetResult(); }
    }

    private void ViewReleases_Click(object sender, RoutedEventArgs e)
    {
        if (_isShuttingDown || _isUpdateSuspended) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AboutInformation.ReleasesUri.AbsoluteUri)
                { UseShellExecute = true });
        }
        catch (Exception)
        {
            System.Windows.MessageBox.Show(this, $"Open the releases page manually:\n\n{AboutInformation.ReleasesUri}",
                "View releases", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void GlobalProtection_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanToggleGlobalProtection) GlobalProtectionRequested?.Invoke();
    }

    private async void GlobalPanic_Click(object sender, RoutedEventArgs e)
    {
        if (_isShuttingDown || _isUpdateSuspended || !ViewModel.CanToggleGlobalPanic || GlobalPanicRequested is null
            || _runtimeActionFinished?.Task.IsCompleted == false) return;
        _runtimeActionFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewModel.IsRuntimeActionBusy = true;
        try
        {
            await GlobalPanicRequested.Invoke();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"The panic action could not be completed.\n\n{exception.Message}",
                "Protection action failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ViewModel.IsRuntimeActionBusy = false;
            _runtimeActionFinished.SetResult();
        }
    }

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public IReadOnlyList<MaskStyleOption> MaskStylesSource => MaskStyleOption.All;

    public Array MaskColorsSource => MaskColors;

    public IReadOnlyList<ActivationModeOption> ActivationModesSource => ActivationModeOption.All;

    public void ReplaceViewModel(SettingsViewModel viewModel)
    {
        if (DataContext is SettingsViewModel previous)
        {
            viewModel.ApplicationSearch = previous.ApplicationSearch;
            viewModel.PanicHelpScope = previous.PanicHelpScope;
            var selected = viewModel.AppProfiles.FirstOrDefault(profile =>
                profile.ProfileId == previous.SelectedApplication?.ProfileId);
            if (selected is not null) viewModel.OpenApplication(selected);
            viewModel.SelectedPage = previous.SelectedPage;
            if (previous.IsAdvancedZonesPage) viewModel.ShowAdvancedZones();
            previous.SettingsEdited -= QueuePreview;
            previous.Dispose();
        }

        DataContext = viewModel;
        viewModel.SettingsEdited += QueuePreview;
    }

    private void QueuePreview()
    {
        if (!IsLoaded || _isSaving || _previewQueued || _allowClose || _isShuttingDown || _isUpdateSuspended) return;
        _previewQueued = true;
        var editedSession = ViewModel;
        Dispatcher.BeginInvoke(() =>
        {
            _previewQueued = false;
            if (!ReferenceEquals(editedSession, DataContext) || !IsVisible || _isSaving || _isShuttingDown || _isUpdateSuspended) return;
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

    internal Task BeginShutdownAsync()
    {
        _isShuttingDown = true;
        IsEnabled = false;
        // The shell's mutation callback can finish before the event handler's
        // final UI continuation. Do not dispose its view model in that gap.
        return Task.WhenAll(_saveFinished?.Task ?? Task.CompletedTask,
            _runtimeActionFinished?.Task ?? Task.CompletedTask,
            _updateCheckFinished?.Task ?? Task.CompletedTask);
    }

    internal bool HasUpdateBlockingEdits => ViewModel.HasPendingChanges || HasBindingErrors(this);

    internal Task SuspendForUpdateAsync()
    {
        _isUpdateSuspended = true;
        IsEnabled = false;
        return Task.WhenAll(_saveFinished?.Task ?? Task.CompletedTask,
            _runtimeActionFinished?.Task ?? Task.CompletedTask,
            _updateCheckFinished?.Task ?? Task.CompletedTask);
    }

    internal void ResumeAfterUpdate()
    {
        _isUpdateSuspended = false;
        IsEnabled = !_isShuttingDown && !_isSaving;
    }

    internal async Task<bool> ResolveUpdateEditsAsync()
    {
        await Task.WhenAll(_saveFinished?.Task ?? Task.CompletedTask,
            _runtimeActionFinished?.Task ?? Task.CompletedTask);
        if (_isShuttingDown || _isUpdateSuspended || OwnedWindows.Cast<Window>().Any(window => window.IsVisible)) return false;
        if (!CommitFocusedEditor() || !CommitGridEditors(this) || HasBindingErrors(this))
        {
            System.Windows.MessageBox.Show(this, "Correct the highlighted settings before updating. Your edits are retained.",
                "Check settings", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!ViewModel.HasPendingChanges) return true;
        var choice = System.Windows.MessageBox.Show(this,
            "Save your changes before updating?\n\nYes: save changes.\nNo: discard changes.\nCancel: keep editing and update later.",
            "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.No) DiscardRequested?.Invoke();
        else if (!await TrySaveAsync()) return false;
        return !HasUpdateBlockingEdits;
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
        FocusSelectedPage();
    }

    private void ManageApplication_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppProfileViewModel profile)
        {
            ViewModel.OpenApplication(profile);
            FocusSelectedPage();
        }
    }

    private void BackToApplications_Click(object sender, RoutedEventArgs e)
    {
        Navigate_Click(new System.Windows.Controls.Button { Tag = DesktopPage.Applications }, e);
    }

    private void AdvancedZones_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitFocusedEditor() || !CommitGridEditors(this) || HasBindingErrors(this)) return;
        ViewModel.ShowAdvancedZones();
        FocusSelectedPage();
    }

    private void BackFromZones_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitFocusedEditor() || !CommitGridEditors(this) || HasBindingErrors(this))
        {
            System.Windows.MessageBox.Show(this, "Correct the highlighted zone value before going back. Your edits are retained.",
                "Check this value", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ViewModel.ShowPrimaryApplicationDetails();
        FocusSelectedPage();
    }

    private void FocusSelectedPage()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) return;
            if (ViewModel.IsApplicationListPage)
            {
                ApplicationSearchBox.Focus();
                return;
            }
            var pageName = ViewModel.IsAdvancedZonesPage ? "AdvancedZonesPage"
                : ViewModel.IsApplicationDetailsPage ? "ApplicationDetailsPage" : $"{ViewModel.SelectedPage}Page";
            if (FindName(pageName) is DependencyObject page) FocusFirstControl(page);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private static bool FocusFirstControl(DependencyObject root)
    {
        if (root is System.Windows.Controls.Control { IsVisible: true, IsEnabled: true, Focusable: true } control
            && control is not ScrollViewer && control.Focus()) return true;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FocusFirstControl(System.Windows.Media.VisualTreeHelper.GetChild(root, index))) return true;
        return false;
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
        await TrySaveAsync();
    }

    private async Task<bool> TrySaveAsync()
    {
        if (SaveRequested is null || _isSaving || _isShuttingDown || _isUpdateSuspended)
        {
            return false;
        }

        if (!CommitFocusedEditor() || !CommitGridEditors(this) || HasBindingErrors(this))
        {
            System.Windows.MessageBox.Show(
                this,
                "One or more fields contain text that cannot be converted to a number. Correct the highlighted value, then save again.",
                "Check these settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var editedSession = ViewModel;
        var settings = editedSession.ToModel();
        var validationErrors = AppSettingsValidator.Validate(settings);
        if (validationErrors.Count > 0)
        {
            ShowValidationErrors(validationErrors);
            return false;
        }

        _isSaving = true;
        _saveFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewModel.IsSaving = true;
        IsEnabled = false;
        try
        {
            await SaveRequested.Invoke(settings);
            // The coordinator may have replaced the session with normalized
            // saved settings and already applied a newer hotkey baseline. Do
            // not overwrite that authority, or mark later edits as persisted.
            if (ReferenceEquals(editedSession, ViewModel)) ViewModel.SetSavedBaseline(settings);
            return true;
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
            return false;
        }
        finally
        {
            IsEnabled = !_isShuttingDown && !_isUpdateSuspended;
            _isSaving = false;
            ViewModel.IsSaving = false;
            _saveFinished.SetResult();
        }
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving || _isShuttingDown || _isUpdateSuspended) return;
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

    private void DeveloperContact_Click(object sender, RoutedEventArgs e)
    {
        var uri = ((sender as FrameworkElement)?.Tag as string) switch
        {
            "email" => AboutInformation.EmailUri,
            "github" => AboutInformation.GitHubUri,
            "linkedin" => AboutInformation.LinkedInUri,
            _ => null,
        };
        if (uri is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"The link could not be opened.\n\n{uri}\n\n{exception.Message}",
                "Developer contact", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CopySupportEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(AboutInformation.SupportEmail);
            ContactFeedback.Text = "Support email copied.";
        }
        catch (Exception)
        {
            ContactFeedback.Text = "Clipboard unavailable. Select and copy the email address above.";
        }
    }

    private void CopyConfigurationPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ViewModel.ConfigPath);
        }
        catch
        {
            System.Windows.MessageBox.Show(this, $"The clipboard is unavailable. Your settings path is:\n\n{ViewModel.ConfigPath}",
                "Copy settings path", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenConfigurationFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(ViewModel.ConfigPath));
            if (folder is null || !System.IO.Directory.Exists(folder))
                throw new System.IO.DirectoryNotFoundException("The settings folder does not exist yet.");
            var startInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(folder);
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"The settings folder could not be opened.\n\n{exception.Message}",
                "Open settings folder", MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
        FocusSelectedPage();
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
            if (!_isSaving && !_isShuttingDown && !_isUpdateSuspended) DiscardAndHide();
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (_isSaving || _isShuttingDown || _isUpdateSuspended) { e.Handled = true; return; }
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
        if (_isSaving || _isShuttingDown || _isUpdateSuspended) return;
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
