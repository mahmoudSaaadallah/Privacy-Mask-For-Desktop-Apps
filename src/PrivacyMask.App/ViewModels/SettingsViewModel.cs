using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private bool _launchAtLogin;
    private bool _startMinimized;

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

    public AppSettings ToModel()
    {
        return new AppSettings
        {
            OnboardingCompleted = true,
            LaunchAtLogin = LaunchAtLogin,
            StartMinimized = StartMinimized,
            CurrentMode = RuntimeMode.Standard,
            GlobalHotkeys = Hotkeys.Select(binding => binding.ToModel()).ToList(),
            AppProfiles = AppProfiles.Select(profile => profile.ToModel()).ToList(),
        };
    }
}
