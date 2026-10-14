using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private bool _selectedFromBrowse;
    private readonly Func<IReadOnlyList<DesktopApplicationCandidate>> _loadCandidates;

    public AddApplicationWindow(
        DesktopApplicationCatalog catalog,
        IReadOnlySet<string> protectedProcessNames,
        string? relinkProfileName = null)
        : this(catalog, protectedProcessNames, relinkProfileName,
            () => catalog.GetAvailableRunningApplications(protectedProcessNames))
    {
    }

    internal AddApplicationWindow(
        DesktopApplicationCatalog catalog,
        IReadOnlySet<string> protectedProcessNames,
        string? relinkProfileName,
        Func<IReadOnlyList<DesktopApplicationCandidate>> loadCandidates)
    {
        _catalog = catalog;
        _protectedProcessNames = protectedProcessNames;
        _loadCandidates = loadCandidates;
        _relinkProfileName = string.IsNullOrWhiteSpace(relinkProfileName)
            ? null
            : relinkProfileName.Trim();
        ApplicationsView = new ListCollectionView(Applications);
        ApplicationsView.Filter = MatchesSearch;
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
    public ICollectionView ApplicationsView { get; }

    private bool MatchesSearch(object item)
    {
        if (item is not ApplicationCandidateViewModel candidate) return false;
        var query = SearchTextBox?.Text.Trim() ?? string.Empty;
        return query.Length == 0
            || candidate.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || candidate.Candidate.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplicationsView.Refresh();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (EmptyStateText is null) return;
        EmptyStateText.Visibility = ApplicationsView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = Applications.Count == 0
            ? "No eligible applications are visible. Open one and refresh, or browse to its executable."
            : "No applications match your search.";
    }

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
            if (!_selectedFromBrowse)
            {
                SelectedApplication = null;
                SelectedTargetText.Text = "Choose an application";
                ConfirmButton.IsEnabled = false;
            }
            return;
        }

        _selectedFromBrowse = false;
        SelectedApplication = selected.Candidate;
        ProfileNameTextBox.Text = selected.DisplayName;
        SelectedTargetText.Text = selected.Candidate.ExecutablePath;
        ConfirmButton.IsEnabled = true;
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
        var candidates = _loadCandidates();
        SelectedApplication = null;
        _selectedFromBrowse = false;
        ConfirmButton.IsEnabled = false;
        SelectedTargetText.Text = "Choose an application";
        ProfileNameTextBox.Clear();
        Applications.Clear();
        foreach (var candidate in candidates)
        {
            Applications.Add(new ApplicationCandidateViewModel(candidate));
        }

        UpdateEmptyState();
        if (ApplicationsList.Items.Count > 0)
        {
            ApplicationsList.SelectedIndex = 0;
        }
    }

    private void SelectCandidate(DesktopApplicationCandidate candidate)
    {
        ApplicationsList.SelectedIndex = -1;
        _selectedFromBrowse = true;
        SelectedApplication = candidate;
        SelectedTargetText.Text = candidate.ExecutablePath;
        ConfirmButton.IsEnabled = true;
        ProfileNameTextBox.Text = candidate.DisplayName;
        ProfileNameTextBox.Focus();
        ProfileNameTextBox.SelectAll();
    }
}
