using System.Collections.Generic;
using System.Linq;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public sealed class DefaultSettingsFactory
{
    public AppSettings Create()
    {
        return new AppSettings
        {
            OnboardingCompleted = false,
            LaunchAtLogin = false,
            StartMinimized = true,
            CurrentMode = RuntimeMode.Standard,
            GlobalHotkeys =
            [
                new HotkeyBinding
                {
                    Action = HotkeyAction.ToggleProtection,
                    DisplayName = "Toggle protection",
                    Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
                    VirtualKey = 0x50,
                },
                new HotkeyBinding
                {
                    Action = HotkeyAction.PanicHideAll,
                    DisplayName = "Panic hide all",
                    Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
                    VirtualKey = 0x48,
                },
                new HotkeyBinding
                {
                    Action = HotkeyAction.OpenSettings,
                    DisplayName = "Open settings",
                    Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
                    VirtualKey = 0x4F,
                },
                new HotkeyBinding
                {
                    Action = HotkeyAction.TemporaryRevealHold,
                    DisplayName = "Hold to reveal",
                    Modifiers = HotkeyModifiers.Alt,
                    VirtualKey = 0x12,
                    IsHoldGesture = true,
                },
            ],
            AppProfiles =
            [
                BuildProfile(AppId.WhatsApp, "WhatsApp Desktop", ["WhatsApp", "WhatsAppBeta"], "whatsapp-wide"),
                BuildProfile(AppId.Telegram, "Telegram Desktop", ["Telegram"], "telegram-wide"),
            ],
        };
    }

    public AppSettings MergeWithDefaults(AppSettings? persisted)
    {
        var defaults = Create();
        if (persisted is null)
        {
            return defaults;
        }

        defaults.OnboardingCompleted = persisted.OnboardingCompleted;
        defaults.LaunchAtLogin = persisted.LaunchAtLogin;
        defaults.StartMinimized = persisted.StartMinimized;
        defaults.CurrentMode = persisted.CurrentMode;
        defaults.GlobalHotkeys = MergeHotkeys(defaults.GlobalHotkeys, persisted.GlobalHotkeys);
        var migrateLegacyFocusAwareProfiles = persisted.Version < 2;
        var migrateLegacyMaskIntensity = persisted.Version < 3;
        var migrateLegacyHoverReveal = persisted.Version < 4;
        var migrateLegacyMaskColor = persisted.Version < 5;

        foreach (var defaultProfile in defaults.AppProfiles)
        {
            var persistedProfile = persisted.AppProfiles.FirstOrDefault(profile => profile.AppId == defaultProfile.AppId);
            if (persistedProfile is null)
            {
                continue;
            }

            defaultProfile.Enabled = persistedProfile.Enabled;
            defaultProfile.StartupMode = migrateLegacyFocusAwareProfiles && persistedProfile.StartupMode == AppActivationMode.FocusAware
                ? AppActivationMode.Manual
                : persistedProfile.StartupMode;
            defaultProfile.MaskIntensity = migrateLegacyMaskIntensity && persistedProfile.MaskIntensity <= 1.01d
                ? 1.35d
                : double.Clamp(persistedProfile.MaskIntensity, 0.60d, 2.40d);
            defaultProfile.MaskColor = migrateLegacyMaskColor
                ? defaultProfile.MaskColor
                : NormalizeMaskColor(persistedProfile.MaskColor, defaultProfile.MaskColor);
            defaultProfile.HoverRevealWidthPixels = migrateLegacyHoverReveal && persistedProfile.HoverRevealWidthPixels <= 0
                ? defaultProfile.HoverRevealWidthPixels
                : int.Clamp(persistedProfile.HoverRevealWidthPixels, 80, 1400);
            defaultProfile.HoverRevealHeightPixels = migrateLegacyHoverReveal && persistedProfile.HoverRevealHeightPixels <= 0
                ? defaultProfile.HoverRevealHeightPixels
                : int.Clamp(persistedProfile.HoverRevealHeightPixels, 20, 420);
            defaultProfile.SelectedPresetId = ResolvePresetId(defaultProfile, persistedProfile.SelectedPresetId);
            defaultProfile.Hotkeys = MergeHotkeys(defaultProfile.Hotkeys, persistedProfile.Hotkeys);
            defaultProfile.Zones = MergeZones(defaultProfile.Presets, defaultProfile.SelectedPresetId, persistedProfile.Zones);
            defaultProfile.WindowMatchers = MergeWindowMatchers(defaultProfile.WindowMatchers, persistedProfile.WindowMatchers);
        }

        return defaults;
    }

    private static AppProfile BuildProfile(AppId appId, string displayName, IEnumerable<string> processNames, string selectedPresetId)
    {
        var presets = PresetCatalog.ClonePresets(PresetCatalog.GetDefaultPresets(appId)).ToList();
        var selectedPreset = presets.First(preset => preset.PresetId == selectedPresetId);

        return new AppProfile
        {
            AppId = appId,
            DisplayName = displayName,
            Enabled = true,
            StartupMode = AppActivationMode.Manual,
            MaskIntensity = 1.35d,
            MaskColor = MaskColorOption.Black,
            HoverRevealWidthPixels = 394,
            HoverRevealHeightPixels = 42,
            WindowMatchers =
            [
                new WindowMatcher
                {
                    ProcessNames = processNames.ToList(),
                },
            ],
            Presets = presets,
            SelectedPresetId = selectedPresetId,
            Zones = selectedPreset.Zones.Select(PresetCatalog.CloneZone).ToList(),
        };
    }

    private static List<WindowMatcher> MergeWindowMatchers(IEnumerable<WindowMatcher> defaults, IEnumerable<WindowMatcher>? persisted)
    {
        if (persisted is null)
        {
            return defaults.Select(CloneWindowMatcher).ToList();
        }

        var merged = persisted.Select(CloneWindowMatcher).ToList();
        return merged.Count > 0 ? merged : defaults.Select(CloneWindowMatcher).ToList();
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

    private static string ResolvePresetId(AppProfile defaultProfile, string persistedPresetId)
    {
        return defaultProfile.Presets.Any(preset => preset.PresetId == persistedPresetId)
            ? persistedPresetId
            : defaultProfile.SelectedPresetId;
    }

    private static List<PrivacyZone> MergeZones(IEnumerable<LayoutPreset> presets, string selectedPresetId, IEnumerable<PrivacyZone>? persistedZones)
    {
        var presetZones = presets.First(preset => preset.PresetId == selectedPresetId).Zones;
        if (persistedZones is null)
        {
            return presetZones.Select(PresetCatalog.CloneZone).ToList();
        }

        var persistedMap = persistedZones.ToDictionary(zone => zone.ZoneId, zone => zone);
        return presetZones
            .Select(defaultZone =>
            {
                if (!persistedMap.TryGetValue(defaultZone.ZoneId, out var persisted))
                {
                    return PresetCatalog.CloneZone(defaultZone);
                }

                return new PrivacyZone
                {
                    ZoneId = defaultZone.ZoneId,
                    DisplayName = persisted.DisplayName,
                    Anchor = persisted.Anchor,
                    RelativeRect = persisted.RelativeRect.Clamp(),
                    Style = persisted.Style,
                    Strength = persisted.Strength,
                    Behavior = persisted.Behavior,
                    Enabled = persisted.Enabled,
                };
            })
            .ToList();
    }

    private static List<HotkeyBinding> MergeHotkeys(IEnumerable<HotkeyBinding> defaults, IEnumerable<HotkeyBinding>? persisted)
    {
        var persistedMap = persisted?.ToDictionary(binding => binding.Action) ?? [];
        return defaults.Select(binding =>
        {
            if (!persistedMap.TryGetValue(binding.Action, out var saved))
            {
                return CloneHotkey(binding);
            }

            return new HotkeyBinding
            {
                Action = binding.Action,
                DisplayName = binding.DisplayName,
                Modifiers = saved.Modifiers,
                VirtualKey = saved.VirtualKey,
                Enabled = saved.Enabled,
                IsHoldGesture = binding.IsHoldGesture,
            };
        }).ToList();
    }

    private static HotkeyBinding CloneHotkey(HotkeyBinding binding)
    {
        return new HotkeyBinding
        {
            Action = binding.Action,
            DisplayName = binding.DisplayName,
            Modifiers = binding.Modifiers,
            VirtualKey = binding.VirtualKey,
            Enabled = binding.Enabled,
            IsHoldGesture = binding.IsHoldGesture,
        };
    }

    private static MaskColorOption NormalizeMaskColor(MaskColorOption persistedColor, MaskColorOption fallback)
    {
        return Enum.IsDefined(typeof(MaskColorOption), persistedColor)
            ? persistedColor
            : fallback;
    }
}
