using System.Windows;
using PrivacyMask.App.Services;

namespace PrivacyMask.App.Windows;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow(bool launchAtLogin, bool startMinimized)
    {
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
        LaunchAtLoginCheckBox.IsChecked = launchAtLogin;
        StartMinimizedCheckBox.IsChecked = startMinimized;
    }

    public bool LaunchAtLogin => LaunchAtLoginCheckBox.IsChecked == true;

    public bool StartMinimized => StartMinimizedCheckBox.IsChecked != false;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
