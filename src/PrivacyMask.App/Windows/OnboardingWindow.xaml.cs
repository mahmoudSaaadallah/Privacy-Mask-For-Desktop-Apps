using System.Windows;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.Windows;

public partial class OnboardingWindow : Window
{
    private bool _showingShortcuts;

    public OnboardingWindow(bool launchAtLogin, bool startMinimized,
        IEnumerable<HotkeyBinding> hotkeys, IReadOnlySet<HotkeyAction> unavailableHotkeys)
    {
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
        LaunchAtLoginCheckBox.IsChecked = launchAtLogin;
        StartMinimizedCheckBox.IsChecked = startMinimized;
        ShortcutStep.ItemsSource = hotkeys.Select(binding =>
            new HotkeyBindingViewModel(binding, unavailableHotkeys.Contains(binding.Action))).ToArray();
    }

    public bool LaunchAtLogin => LaunchAtLoginCheckBox.IsChecked == true;

    public bool StartMinimized => StartMinimizedCheckBox.IsChecked != false;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_showingShortcuts) Finish_Click(sender, e);
        else ShowStep(showShortcuts: true);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(showShortcuts: false);

    private void ShowStep(bool showShortcuts)
    {
        _showingShortcuts = showShortcuts;
        WelcomeStep.Visibility = SkipButton.Visibility = showShortcuts ? Visibility.Collapsed : Visibility.Visible;
        ShortcutStep.Visibility = BackButton.Visibility = showShortcuts ? Visibility.Visible : Visibility.Collapsed;
        StepIndicator.Text = showShortcuts ? "Step 2 of 2" : "Step 1 of 2";
        HeadingText.Text = showShortcuts ? "Keyboard shortcuts" : "Welcome to PrivacyMask";
        SubtitleText.Text = showShortcuts
            ? "Use these keys while you work. You can review them later on the Shortcuts page."
            : "Keep your screen private, on your terms.";
        ContinueButton.Content = showShortcuts ? "_Get started" : "_Next: shortcuts";
        TourScroll.ScrollToTop();
        HeadingText.Focus();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
