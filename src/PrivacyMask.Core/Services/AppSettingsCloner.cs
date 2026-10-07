using System.Linq;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class AppSettingsCloner
{
    public static AppSettings Clone(AppSettings settings)
    {
        return new AppSettings
        {
            Version = settings.Version,
            OnboardingCompleted = settings.OnboardingCompleted,
            LaunchAtLogin = settings.LaunchAtLogin,
            StartMinimized = settings.StartMinimized,
            CurrentMode = settings.CurrentMode,
            GlobalHotkeys = settings.GlobalHotkeys.Select(CloneHotkey).ToList(),
            AppProfiles = settings.AppProfiles.Select(CloneProfile).ToList(),
        };
    }

    private static AppProfile CloneProfile(AppProfile profile)
    {
        return new AppProfile
        {
            AppId = profile.AppId,
            DisplayName = profile.DisplayName,
            Enabled = profile.Enabled,
            StartupMode = profile.StartupMode,
            MaskIntensity = profile.MaskIntensity,
            MaskColor = profile.MaskColor,
            HoverRevealWidthPixels = profile.HoverRevealWidthPixels,
            HoverRevealHeightPixels = profile.HoverRevealHeightPixels,
            WindowMatchers = profile.WindowMatchers.Select(CloneWindowMatcher).ToList(),
            Zones = profile.Zones.Select(PresetCatalog.CloneZone).ToList(),
            Hotkeys = profile.Hotkeys.Select(CloneHotkey).ToList(),
            Presets = profile.Presets.Select(PresetCatalog.ClonePreset).ToList(),
            SelectedPresetId = profile.SelectedPresetId,
        };
    }

    private static WindowMatcher CloneWindowMatcher(WindowMatcher matcher)
    {
        return new WindowMatcher
        {
            ProcessNames = [.. matcher.ProcessNames],
            TitleContains = matcher.TitleContains,
            ClassNameContains = matcher.ClassNameContains,
        };
    }

    private static HotkeyBinding CloneHotkey(HotkeyBinding binding)
    {
        return new HotkeyBinding
        {
            Action = binding.Action,
            Modifiers = binding.Modifiers,
            VirtualKey = binding.VirtualKey,
            Enabled = binding.Enabled,
            IsHoldGesture = binding.IsHoldGesture,
            DisplayName = binding.DisplayName,
        };
    }
}
