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

namespace PrivacyMask.App;

public partial class MainWindow : Window
{
    public static readonly Array MaskColors = Enum.GetValues<MaskColorOption>();
    public static readonly Array ActivationModes = Enum.GetValues<AppActivationMode>();

    private bool _allowClose;
    private bool _isSaving;

    public MainWindow(SettingsViewModel viewModel)
    {
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

    public Array ActivationModesSource => ActivationModes;

    public void ReplaceViewModel(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SaveRequested is null || _isSaving)
        {
            return;
        }

        if (!CommitFocusedEditor() || HasBindingErrors(this))
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
        IsEnabled = false;
        try
        {
            await SaveRequested.Invoke(settings);
            Hide();
        }
        catch (Exception exception)
        {
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
        }
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        DiscardAndHide();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppProfileViewModel profile)
        {
            profile.ApplySelectedPreset();
            PreviewRequested?.Invoke(ViewModel.ToModel());
        }
    }

    private void MaskIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || sender is not Slider)
        {
            return;
        }

        PreviewRequested?.Invoke(ViewModel.ToModel());
    }

    private void HoverRevealDimension_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.TextBox)
        {
            return;
        }

        PreviewRequested?.Invoke(ViewModel.ToModel());
    }

    private void MaskColorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.ComboBox)
        {
            return;
        }

        PreviewRequested?.Invoke(ViewModel.ToModel());
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
