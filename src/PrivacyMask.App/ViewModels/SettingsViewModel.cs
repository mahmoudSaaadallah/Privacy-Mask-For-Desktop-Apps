using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private bool _launchAtLogin;
    private bool _startMinimized;
    private PanicPinCredential? _panicPin;
    private DesktopPage _selectedPage = DesktopPage.Overview;

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

    public static SettingsViewModel FromModel(
        AppSettings settings,
        RuntimeMode currentMode,
        string configPath,
        IReadOnlySet<HotkeyAction> unavailableHotkeyActions)
    {
        return new SettingsViewModel
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
            ConfigPath = $"Config file: {configPath}",
            Hotkeys = new ObservableCollection<HotkeyBindingViewModel>(settings.GlobalHotkeys.Select(
                binding => new HotkeyBindingViewModel(binding, unavailableHotkeyActions.Contains(binding.Action)))),
            AppProfiles = new ObservableCollection<AppProfileViewModel>(settings.AppProfiles.Select(profile => new AppProfileViewModel(profile))),
        };
    }

    public string ConfigPath { get; private set; } = string.Empty;

    public string CurrentModeSummary { get; private set; } = string.Empty;

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

    public void SetPanicPin(PanicPinCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _panicPin = PanicPinService.Clone(credential);
        RaisePropertyChanged(nameof(PanicPin));
        RaisePropertyChanged(nameof(HasPanicPin));
        RaisePropertyChanged(nameof(PanicPinStatus));
        RaisePropertyChanged(nameof(PanicPinActionLabel));
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
