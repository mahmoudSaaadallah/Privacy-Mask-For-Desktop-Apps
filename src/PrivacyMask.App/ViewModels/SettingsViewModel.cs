using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private bool _launchAtLogin;
    private bool _startMinimized;
    private PanicPinCredential? _panicPin;
    private DesktopPage _selectedPage = DesktopPage.Overview;
    private SettingsChangeTracker? _changeTracker;
    private bool _isSaving;
    private string? _saveError;
    private string _applicationSearch = string.Empty;
    private AppProfileViewModel? _selectedApplication;
    private RuntimeMode _runtimeMode;
    private bool _isRuntimeActionBusy;
    private readonly HashSet<AppProfileViewModel> _overviewProfiles = [];
    private PanicHelpScope _panicHelpScope;

    public PanicHelpScope PanicHelpScope
    {
        get => _panicHelpScope;
        set
        {
            if (SetProperty(ref _panicHelpScope, value)) RaisePropertyChanged(nameof(PanicScopeSummary));
        }
    }
    public string PanicScopeSummary => PanicHelpScope == PanicHelpScope.ActiveApp
        ? "Use the focused-app panic shortcut. A correct PIN is required to unlock."
        : "Use Panic mask all in Overview or the tray. A correct PIN is required to unlock.";
    public IReadOnlyList<HotkeyBindingViewModel> ShortcutRows => Hotkeys.OrderBy(binding => binding.Action switch
    {
        HotkeyAction.ToggleProtection => 0,
        HotkeyAction.PanicHideAll => 1,
        HotkeyAction.IncreaseBlurStrength => 2,
        HotkeyAction.DecreaseBlurStrength => 3,
        HotkeyAction.TemporaryRevealHold => 4,
        _ => 5,
    }).ToArray();

    public int ConfiguredApplicationCount => AppProfiles.Count;
    public ObservableCollection<AppProfileViewModel> OverviewAppProfiles { get; } = [];
    public int EnabledApplicationCount => AppProfiles.Count(profile => profile.Enabled);
    public int ProtectedApplicationCount => AppProfiles.Count(profile => profile.RuntimeStatus is
        ProfileRuntimeStatus.Protected or ProfileRuntimeStatus.PanicProtected);
    public bool HasNoApplications => AppProfiles.Count == 0;
    public bool HasMoreOverviewApplications => AppProfiles.Count > 6;

    private void RefreshOverview()
    {
        RaisePropertyChanged(nameof(ConfiguredApplicationCount));
        RaisePropertyChanged(nameof(EnabledApplicationCount));
        RaisePropertyChanged(nameof(ProtectedApplicationCount));
        RaisePropertyChanged(nameof(HasNoApplications));
        RaisePropertyChanged(nameof(HasMoreOverviewApplications));
    }

    private void OverviewProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var profile in _overviewProfiles) profile.PropertyChanged -= OverviewProfileChanged;
        _overviewProfiles.Clear();
        foreach (var profile in AppProfiles)
        {
            _overviewProfiles.Add(profile);
            profile.PropertyChanged += OverviewProfileChanged;
        }
        // Keep the dashboard small; the Applications page virtualizes the full list.
        var visible = AppProfiles.Take(6).ToList();
        if (!OverviewAppProfiles.SequenceEqual(visible))
        {
            OverviewAppProfiles.Clear();
            foreach (var profile in visible) OverviewAppProfiles.Add(profile);
        }
        RefreshOverview();
    }

    private void OverviewProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppProfileViewModel.RuntimeStatus) or nameof(AppProfileViewModel.Enabled))
            RefreshOverview();
    }

    public bool IsRuntimeActionBusy
    {
        get => _isRuntimeActionBusy;
        set
        {
            if (SetProperty(ref _isRuntimeActionBusy, value)) NotifyRuntimeControls();
        }
    }
    public string ProtectionHeading => _runtimeMode switch
    {
        RuntimeMode.Off => "Global protection paused",
        RuntimeMode.Panic => "Global panic mask active",
        _ => "Global protection active",
    };
    public string GlobalPauseLabel => _runtimeMode == RuntimeMode.Off ? "Resume all" : "Pause all";
    public string ProtectionBannerText => _runtimeMode switch
    {
        RuntimeMode.Off => "Protection paused",
        RuntimeMode.Panic => "Panic mask active",
        _ => "Protection active",
    };
    public string ProtectionBannerBackground => _runtimeMode switch { RuntimeMode.Off => "#FFF3DA", RuntimeMode.Panic => "#FFE4E1", _ => "#DCF4EB" };
    public string ProtectionBannerColor => _runtimeMode switch { RuntimeMode.Off => "#BD8000", RuntimeMode.Panic => "#D93A32", _ => "#009D6C" };
    public string QuickShortcutGesture => Hotkeys.FirstOrDefault(binding => binding.Action == HotkeyAction.ToggleProtection)?.GestureText ?? "Not configured";
    public IReadOnlyList<ShortcutKeycap> QuickShortcutKeys => QuickShortcutGesture.Split(" + ")
        .Select((text, index) => new ShortcutKeycap(text, index > 0)).ToArray();
    public string GlobalPanicLabel => _runtimeMode == RuntimeMode.Panic ? "Unlock panic mask all" : "Panic mask all";
    public bool CanToggleGlobalProtection => !IsRuntimeActionBusy && _runtimeMode != RuntimeMode.Panic;
    public bool CanToggleGlobalPanic => !IsRuntimeActionBusy;
    private void NotifyRuntimeControls()
    {
        RaisePropertyChanged(nameof(ProtectionBannerText));
        RaisePropertyChanged(nameof(ProtectionBannerBackground));
        RaisePropertyChanged(nameof(ProtectionBannerColor));
        RaisePropertyChanged(nameof(ProtectionHeading));
        RaisePropertyChanged(nameof(GlobalPauseLabel));
        RaisePropertyChanged(nameof(GlobalPanicLabel));
        RaisePropertyChanged(nameof(CanToggleGlobalProtection));
        RaisePropertyChanged(nameof(CanToggleGlobalPanic));
    }

    public ObservableCollection<AppProfileViewModel> FilteredAppProfiles { get; } = [];
    public string ApplicationSearch
    {
        get => _applicationSearch;
        set
        {
            if (SetProperty(ref _applicationSearch, value ?? string.Empty)) RefreshApplicationFilter();
        }
    }
    public AppProfileViewModel? SelectedApplication
    {
        get => _selectedApplication;
        private set
        {
            if (SetProperty(ref _selectedApplication, value)) NotifyApplicationPages();
        }
    }
    public bool IsApplicationListPage => IsApplicationsPage && SelectedApplication is null;
    public bool IsApplicationDetailsPage => IsApplicationsPage && SelectedApplication is not null;
    public bool HasNoMatchingApplications => FilteredAppProfiles.Count == 0;
    public string ApplicationEmptyMessage => string.IsNullOrWhiteSpace(ApplicationSearch)
        ? "No applications configured. Add an application to start protecting it."
        : "No applications match your search.";

    public void OpenApplication(AppProfileViewModel profile)
    {
        if (!AppProfiles.Contains(profile)) return;
        SelectedPage = DesktopPage.Applications;
        SelectedApplication = profile;
    }

    public void ShowApplicationList()
    {
        SelectedApplication = null;
        SelectedPage = DesktopPage.Applications;
    }

    private void NotifyApplicationPages()
    {
        RaisePropertyChanged(nameof(IsSaveBarVisible));
        RaisePropertyChanged(nameof(IsApplicationListPage));
        RaisePropertyChanged(nameof(IsApplicationDetailsPage));
    }

    private void RefreshApplicationFilter()
    {
        var query = ApplicationSearch.Trim();
        var matches = AppProfiles.Where(profile => query.Length == 0
            || profile.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || profile.WindowMatchers.SelectMany(matcher => matcher.ProcessNames)
                .Any(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        if (!FilteredAppProfiles.SequenceEqual(matches))
        {
            FilteredAppProfiles.Clear();
            foreach (var profile in matches) FilteredAppProfiles.Add(profile);
        }
        if (SelectedApplication is not null && !AppProfiles.Contains(SelectedApplication)) SelectedApplication = null;
        RaisePropertyChanged(nameof(HasNoMatchingApplications));
        RaisePropertyChanged(nameof(ApplicationEmptyMessage));
    }

    public bool HasPendingChanges => _changeTracker?.HasChanges == true;
    public bool IsSaveBarVisible => HasPendingChanges || IsApplicationDetailsPage;
    public bool CanSave => HasPendingChanges && !IsSaving;
    public string EditStatus => IsSaving ? "Saving…"
        : SaveError is not null ? "Save failed — your edits are retained"
        : HasPendingChanges ? "Unsaved changes" : "All changes saved";
    public bool IsSaving
    {
        get => _isSaving;
        set
        {
            if (SetProperty(ref _isSaving, value))
            {
                RaisePropertyChanged(nameof(CanSave));
                RaisePropertyChanged(nameof(EditStatus));
            }
        }
    }
    public string? SaveError
    {
        get => _saveError;
        set
        {
            if (SetProperty(ref _saveError, value)) RaisePropertyChanged(nameof(EditStatus));
        }
    }
    public event Action? SettingsEdited;

    public void SetSavedBaseline(AppSettings settings)
    {
        _changeTracker?.SetBaseline(settings);
    }

    private void SettingsChanged(bool edited)
    {
        if (edited)
        {
            RefreshApplicationFilter();
            foreach (var profile in AppProfiles) profile.NotifyAppearanceChanged();
        }
        if (edited || !HasPendingChanges) _saveError = null;
        RaisePropertyChanged(nameof(HasPendingChanges));
        RaisePropertyChanged(nameof(CanSave));
        RaisePropertyChanged(nameof(EditStatus));
        if (edited) SettingsEdited?.Invoke();
        RaisePropertyChanged(nameof(IsSaveBarVisible));
    }

    public void Dispose()
    {
        AppProfiles.CollectionChanged -= OverviewProfilesChanged;
        foreach (var profile in _overviewProfiles) profile.PropertyChanged -= OverviewProfileChanged;
        _overviewProfiles.Clear();
        if (_changeTracker is not null)
        {
            _changeTracker.Changed -= SettingsChanged;
            _changeTracker.Dispose();
        }
    }

    public DesktopPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                RaisePropertyChanged(nameof(PageTitle));
                RaisePropertyChanged(nameof(IsOverviewPage));
                RaisePropertyChanged(nameof(IsApplicationsPage));
                RaisePropertyChanged(nameof(IsShortcutsPage));
                RaisePropertyChanged(nameof(IsSecurityPage));
                RaisePropertyChanged(nameof(IsSettingsPage));
                RaisePropertyChanged(nameof(IsAboutPage));
                RaisePropertyChanged(nameof(IsSecondaryDesignPage));
                NotifyApplicationPages();
            }
        }
    }

    public string PageTitle => SelectedPage.ToString();
    public bool IsOverviewPage => SelectedPage == DesktopPage.Overview;
    public bool IsApplicationsPage => SelectedPage == DesktopPage.Applications;
    public bool IsShortcutsPage => SelectedPage == DesktopPage.Shortcuts;
    public bool IsSecurityPage => SelectedPage == DesktopPage.Security;
    public bool IsSettingsPage => SelectedPage == DesktopPage.Settings;
    public bool IsAboutPage => SelectedPage == DesktopPage.About;
    public bool IsSecondaryDesignPage => IsShortcutsPage || IsSecurityPage || IsSettingsPage;

    public static SettingsViewModel FromModel(
        AppSettings settings,
        RuntimeMode currentMode,
        string configPath,
        IReadOnlySet<HotkeyAction> unavailableHotkeyActions)
    {
        var viewModel = new SettingsViewModel
        {
            _launchAtLogin = settings.LaunchAtLogin,
            _startMinimized = settings.StartMinimized,
            _panicPin = PanicPinService.Clone(settings.PanicPin),
            CurrentModeSummary = currentMode switch
            {
                RuntimeMode.Standard => "Standard masking is active when a supported app is detected.",
                RuntimeMode.Off => "Protection is paused until you toggle it back on.",
                RuntimeMode.Panic => "Panic mode is fully masking every protected window.",
                RuntimeMode.TemporaryReveal => "Temporary reveal is active while the reveal key is held.",
                _ => "Standard masking is active when a supported app is detected.",
            },
            ConfigPath = configPath,
            Hotkeys = new ObservableCollection<HotkeyBindingViewModel>(settings.GlobalHotkeys.Select(
                binding => new HotkeyBindingViewModel(binding, unavailableHotkeyActions.Contains(binding.Action)))),
            AppProfiles = new ObservableCollection<AppProfileViewModel>(settings.AppProfiles.Select(profile => new AppProfileViewModel(profile))),
        };
        viewModel._changeTracker = new SettingsChangeTracker(viewModel);
        viewModel._changeTracker.Changed += viewModel.SettingsChanged;
        viewModel.RefreshApplicationFilter();
        viewModel.AppProfiles.CollectionChanged += viewModel.OverviewProfilesChanged;
        viewModel.OverviewProfilesChanged(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        viewModel.UpdateRuntimeMode(currentMode);
        return viewModel;
    }

    public string ConfigPath { get; private set; } = string.Empty;

    public string CurrentModeSummary { get; private set; } = string.Empty;

    public void UpdateRuntimeMode(RuntimeMode mode)
    {
        _runtimeMode = mode;
        CurrentModeSummary = mode switch
        {
            RuntimeMode.Off => "Protection is paused for every application.",
            RuntimeMode.Panic => "Panic masks are active for every protected application.",
            _ => "Protection is active for enabled application profiles.",
        };
        RaisePropertyChanged(nameof(CurrentModeSummary));
        NotifyRuntimeControls();
    }

    public ObservableCollection<HotkeyBindingViewModel> Hotkeys { get; private set; } = [];

    public ObservableCollection<AppProfileViewModel> AppProfiles { get; private set; } = [];

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set => SetProperty(ref _launchAtLogin, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public PanicPinCredential? PanicPin => PanicPinService.Clone(_panicPin);

    public bool HasPanicPin => _panicPin is not null;

    public string PanicPinStatus => HasPanicPin
        ? "PIN configured. It is required to disable any panic mask."
        : "No PIN configured. The first panic action will ask you to create one.";

    public string PanicPinActionLabel => HasPanicPin ? "Change PIN" : "Add PIN";
    public string PanicPinCompactStatus => HasPanicPin ? "PIN configured" : "No PIN configured";
    public string PanicPinIndicator => HasPanicPin ? "✓" : "!";
    public string PanicPinIndicatorColor => HasPanicPin ? "#00999E" : "#D99A1F";

    public void SetPanicPin(PanicPinCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _panicPin = PanicPinService.Clone(credential);
        RaisePropertyChanged(nameof(PanicPin));
        RaisePropertyChanged(nameof(HasPanicPin));
        RaisePropertyChanged(nameof(PanicPinStatus));
        RaisePropertyChanged(nameof(PanicPinActionLabel));
        RaisePropertyChanged(nameof(PanicPinCompactStatus));
        RaisePropertyChanged(nameof(PanicPinIndicator));
        RaisePropertyChanged(nameof(PanicPinIndicatorColor));
    }

    public AppSettings ToModel()
    {
        return new AppSettings
        {
            OnboardingCompleted = true,
            LaunchAtLogin = LaunchAtLogin,
            StartMinimized = StartMinimized,
            CurrentMode = RuntimeMode.Standard,
            PanicPin = PanicPinService.Clone(_panicPin),
            GlobalHotkeys = Hotkeys.Select(binding => binding.ToModel()).ToList(),
            AppProfiles = AppProfiles.Select(profile => profile.ToModel()).ToList(),
        };
    }
}

public sealed record ShortcutKeycap(string Text, bool HasSeparator);

public enum PanicHelpScope { ActiveApp, AllApps }
