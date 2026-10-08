using System;
using System.Diagnostics;
using System.Windows;
using PrivacyMask.App.Services;

namespace PrivacyMask.App.Windows;

public partial class AboutWindow : Window
{
    private static readonly Uri EmailUri = new("mailto:mahmoud.saadallah73@gmail.com");
    private static readonly Uri GitHubUri = new("https://github.com/mahmoudsaaadallah");
    private static readonly Uri LinkedInUri = new("https://linkedin.com/in/mahmoudsaaadallah");

    public AboutWindow()
    {
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
    }

    public string VersionText => $"Version {typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void EmailLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(EmailUri);
    }

    private void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(GitHubUri);
    }

    private void LinkedInLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(LinkedInUri);
    }

    private static void OpenUri(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            System.Windows.MessageBox.Show(
                "The link could not be opened automatically on this device.",
                "PrivacyMask",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
