using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Windows.Models;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.App.Windows;

public partial class AddApplicationWindow : Window
{
    private readonly DesktopApplicationCatalog _catalog;
    private readonly IReadOnlySet<string> _protectedProcessNames;

    public AddApplicationWindow(
        DesktopApplicationCatalog catalog,
        IReadOnlySet<string> protectedProcessNames)
    {
        _catalog = catalog;
        _protectedProcessNames = protectedProcessNames;
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
        DataContext = this;
        RefreshApplications();
    }

    public ObservableCollection<ApplicationCandidateViewModel> Applications { get; } = [];

    public DesktopApplicationCandidate? SelectedApplication { get; private set; }

    public string ProfileDisplayName { get; private set; } = string.Empty;

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshApplications();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an application executable",
            Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!_catalog.TryCreateFromExecutable(
            dialog.FileName,
            _protectedProcessNames,
            out var candidate,
            out var failureReason)
            || candidate is null)
        {
            System.Windows.MessageBox.Show(
                this,
                failureReason,
                "Application cannot be added",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectCandidate(candidate);
    }

    private void ApplicationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApplicationsList.SelectedItem is not ApplicationCandidateViewModel selected)
        {
            return;
        }

        SelectedApplication = selected.Candidate;
        ProfileNameTextBox.Text = selected.DisplayName;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApplication is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "Choose an application first.",
                "No application selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var displayName = ProfileNameTextBox.Text.Trim();
        if (displayName.Length == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Enter a name for this application profile.",
                "Profile name required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ProfileNameTextBox.Focus();
            return;
        }

        ProfileDisplayName = displayName;
        DialogResult = true;
    }

    private void RefreshApplications()
    {
        var candidates = _catalog.GetAvailableRunningApplications(_protectedProcessNames);
        SelectedApplication = null;
        ProfileNameTextBox.Clear();
        Applications.Clear();
        foreach (var candidate in candidates)
        {
            Applications.Add(new ApplicationCandidateViewModel(candidate));
        }

        EmptyStateText.Visibility = Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Applications.Count > 0)
        {
            ApplicationsList.SelectedIndex = 0;
        }
    }

    private void SelectCandidate(DesktopApplicationCandidate candidate)
    {
        SelectedApplication = candidate;
        ProfileNameTextBox.Text = candidate.DisplayName;
        ProfileNameTextBox.Focus();
        ProfileNameTextBox.SelectAll();
    }
}
