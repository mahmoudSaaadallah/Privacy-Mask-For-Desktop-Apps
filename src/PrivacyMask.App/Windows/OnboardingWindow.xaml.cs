using System.Windows;
using PrivacyMask.App.Services;

namespace PrivacyMask.App.Windows;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow(bool startMinimized)
    {
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
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
