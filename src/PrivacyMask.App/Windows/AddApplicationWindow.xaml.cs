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
    private readonly string? _relinkProfileName;

    public AddApplicationWindow(
        DesktopApplicationCatalog catalog,
        IReadOnlySet<string> protectedProcessNames,
        string? relinkProfileName = null)
    {
        _catalog = catalog;
        _protectedProcessNames = protectedProcessNames;
        _relinkProfileName = string.IsNullOrWhiteSpace(relinkProfileName)
            ? null
            : relinkProfileName.Trim();
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
        DataContext = this;
        if (_relinkProfileName is not null)
        {
            Title = $"Relink {_relinkProfileName}";
            HeadingText.Text = "Relink application";
            DescriptionText.Text = $"Choose the current executable for {_relinkProfileName}. Its mask appearance and privacy settings will be preserved.";
            ProfileNamePanel.Visibility = Visibility.Collapsed;
            ConfirmButton.Content = "_Relink application";
        }

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
                _relinkProfileName is null ? "Application cannot be added" : "Application cannot be relinked",
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
                _relinkProfileName is null ? "No application selected" : "No replacement selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var displayName = _relinkProfileName ?? ProfileNameTextBox.Text.Trim();
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
