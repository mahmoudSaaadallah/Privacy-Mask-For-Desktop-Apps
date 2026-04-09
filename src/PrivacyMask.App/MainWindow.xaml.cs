using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App;

public partial class MainWindow : Window
{
    public static readonly Array MaskStyles = Enum.GetValues<MaskStyle>();
    public static readonly Array ActivationModes = Enum.GetValues<AppActivationMode>();

    private bool _allowClose;

    public MainWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ReplaceViewModel(viewModel);
    }

    public event Func<AppSettings, Task>? SaveRequested;

    public event Action<AppSettings>? PreviewRequested;

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public Array MaskStylesSource => MaskStyles;

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
        if (SaveRequested is null)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            await SaveRequested.Invoke(ViewModel.ToModel());
            Hide();
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
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

    private void NumericTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
