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
                    DisplayName = "Panic mask all",
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
                new HotkeyBinding
                {
                    Action = HotkeyAction.IncreaseBlurStrength,
                    DisplayName = "Increase blur strength",
                    Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Windows,
                    VirtualKey = 0x26,
                },
                new HotkeyBinding
                {
                    Action = HotkeyAction.DecreaseBlurStrength,
                    DisplayName = "Decrease blur strength",
                    Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Windows,
                    VirtualKey = 0x28,
                },
            ],
            AppProfiles =
            [
                BuildProfile(KnownProfileIds.WhatsApp, AppId.WhatsApp, "WhatsApp Desktop", ["WhatsApp", "WhatsAppBeta"], "whatsapp-wide"),
                BuildProfile(KnownProfileIds.Telegram, AppId.Telegram, "Telegram Desktop", ["Telegram"], "telegram-wide"),
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
        defaults.CurrentMode = RuntimeMode.Standard;
        defaults.GlobalHotkeys = MergeHotkeys(defaults.GlobalHotkeys, persisted.GlobalHotkeys);
        var migrateLegacyFocusAwareProfiles = persisted.Version < 2;
        var migrateLegacyMaskIntensity = persisted.Version < 3;
        var migrateLegacyHoverReveal = persisted.Version < 4;
        var migrateLegacyMaskColor = persisted.Version < 5;
        var migrateLegacySurfaceIntensity = persisted.Version < 7;

        foreach (var defaultProfile in defaults.AppProfiles)
        {
            var persistedProfile = persisted.AppProfiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, defaultProfile.ProfileId, StringComparison.OrdinalIgnoreCase))
                ?? persisted.AppProfiles.FirstOrDefault(profile => profile.AppId == defaultProfile.AppId);
            if (persistedProfile is null)
            {
                continue;
            }

            defaultProfile.Enabled = persistedProfile.Enabled;
            defaultProfile.StartupMode = migrateLegacyFocusAwareProfiles && persistedProfile.StartupMode == AppActivationMode.FocusAware
                ? AppActivationMode.Manual
                : persistedProfile.StartupMode;
            var persistedMaskIntensity = migrateLegacyMaskIntensity && persistedProfile.MaskIntensity <= 1.01d
                ? 1.35d
                : persistedProfile.MaskIntensity;
            defaultProfile.MaskIntensity = migrateLegacySurfaceIntensity
                ? MaskIntensityScale.FromLegacy(persistedMaskIntensity)
                : MaskIntensityScale.Clamp(persistedMaskIntensity);
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
            defaultProfile.Zones = MergeZones(
                defaultProfile.Presets,
                defaultProfile.SelectedPresetId,
                persistedProfile.Zones,
                migrateLegacySurfaceIntensity);
            defaultProfile.WindowMatchers = MergeWindowMatchers(defaultProfile.WindowMatchers, persistedProfile.WindowMatchers);
        }

        var profileIds = defaults.AppProfiles
            .Select(profile => profile.ProfileId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var customProfile in persisted.AppProfiles.Where(profile => profile.AppId == AppId.Custom))
        {
            var profileId = ResolveCustomProfileId(customProfile.ProfileId, profileIds);
            defaults.AppProfiles.Add(NormalizeCustomProfile(customProfile, profileId));
            profileIds.Add(profileId);
        }

        return defaults;
    }

    private static AppProfile BuildProfile(
        string profileId,
        AppId appId,
        string displayName,
        IEnumerable<string> processNames,
        string selectedPresetId)
    {
        var presets = PresetCatalog.ClonePresets(PresetCatalog.GetDefaultPresets(appId)).ToList();
        var selectedPreset = presets.First(preset => preset.PresetId == selectedPresetId);

        return new AppProfile
        {
            ProfileId = profileId,
            AppId = appId,
            DisplayName = displayName,
            Enabled = true,
            StartupMode = AppActivationMode.Manual,
            MaskIntensity = MaskIntensityScale.Default,
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

    private static string ResolveCustomProfileId(string persistedProfileId, IReadOnlySet<string> existingProfileIds)
    {
        var candidate = string.IsNullOrWhiteSpace(persistedProfileId)
            ? string.Empty
            : persistedProfileId.Trim();
        return candidate.Length > 0 && !existingProfileIds.Contains(candidate)
            ? candidate
            : $"custom-{Guid.NewGuid():N}";
    }

    private static AppProfile NormalizeCustomProfile(AppProfile persisted, string profileId)
    {
        var defaultProfile = CustomAppProfileFactory.Create(
            string.IsNullOrWhiteSpace(persisted.DisplayName) ? "Custom app" : persisted.DisplayName,
            persisted.WindowMatchers
                .SelectMany(matcher => matcher.ProcessNames)
                .FirstOrDefault(processName => !string.IsNullOrWhiteSpace(processName))
                ?? "invalid-custom-profile",
            profileId);
        var matchers = persisted.WindowMatchers
            .Where(matcher => matcher.ProcessNames.Any(processName => !string.IsNullOrWhiteSpace(processName)))
            .Select(matcher => new WindowMatcher
            {
                ProcessNames = matcher.ProcessNames
                    .Where(processName => !string.IsNullOrWhiteSpace(processName))
                    .Select(processName => processName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ProcessNameMatchMode = ProcessNameMatchMode.Exact,
                TitleContains = string.IsNullOrWhiteSpace(matcher.TitleContains) ? null : matcher.TitleContains.Trim(),
                ClassNameContains = string.IsNullOrWhiteSpace(matcher.ClassNameContains) ? null : matcher.ClassNameContains.Trim(),
            })
            .ToList();
        var presets = persisted.Presets.Count > 0
            ? persisted.Presets.Select(NormalizeCustomPreset).ToList()
            : defaultProfile.Presets;
        var selectedPresetId = presets.Any(preset => preset.PresetId == persisted.SelectedPresetId)
            ? persisted.SelectedPresetId
            : presets[0].PresetId;
        var selectedPreset = presets.First(preset => preset.PresetId == selectedPresetId);
        var zones = persisted.Zones.Count > 0
            ? persisted.Zones.Select(zone => NormalizeCustomZone(zone, defaultProfile.Zones[0])).ToList()
            : selectedPreset.Zones.Select(PresetCatalog.CloneZone).ToList();

        return new AppProfile
        {
            ProfileId = profileId,
            AppId = AppId.Custom,
            DisplayName = defaultProfile.DisplayName,
            Enabled = persisted.Enabled && matchers.Count > 0,
            StartupMode = persisted.StartupMode,
            MaskIntensity = MaskIntensityScale.Clamp(persisted.MaskIntensity),
            MaskColor = NormalizeMaskColor(persisted.MaskColor, MaskColorOption.Black),
            HoverRevealWidthPixels = int.Clamp(persisted.HoverRevealWidthPixels, 80, 1400),
            HoverRevealHeightPixels = int.Clamp(persisted.HoverRevealHeightPixels, 20, 420),
            WindowMatchers = matchers,
            Zones = zones,
            Hotkeys = persisted.Hotkeys.Select(CloneHotkey).ToList(),
            Presets = presets,
            SelectedPresetId = selectedPresetId,
        };
    }

    private static LayoutPreset NormalizeCustomPreset(LayoutPreset preset)
    {
        var fallbackZone = PresetCatalog.GetDefaultPresets(AppId.Custom).Single().Zones.Single();
        return new LayoutPreset
        {
            PresetId = string.IsNullOrWhiteSpace(preset.PresetId) ? $"custom-{Guid.NewGuid():N}" : preset.PresetId.Trim(),
            AppId = AppId.Custom,
            DisplayName = string.IsNullOrWhiteSpace(preset.DisplayName) ? "Custom layout" : preset.DisplayName.Trim(),
            LayoutVariant = string.IsNullOrWhiteSpace(preset.LayoutVariant) ? "custom" : preset.LayoutVariant.Trim(),
            MinWindowWidth = Math.Max(1d, preset.MinWindowWidth),
            MinWindowHeight = Math.Max(1d, preset.MinWindowHeight),
            Zones = preset.Zones.Count > 0
                ? preset.Zones.Select(zone => NormalizeCustomZone(zone, fallbackZone)).ToList()
                : [PresetCatalog.CloneZone(fallbackZone)],
        };
    }

    private static PrivacyZone NormalizeCustomZone(PrivacyZone zone, PrivacyZone fallback)
    {
        return new PrivacyZone
        {
            ZoneId = string.IsNullOrWhiteSpace(zone.ZoneId) ? $"zone-{Guid.NewGuid():N}" : zone.ZoneId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(zone.DisplayName) ? "Privacy zone" : zone.DisplayName.Trim(),
            Anchor = zone.Anchor,
            RelativeRect = zone.RelativeRect.Clamp(),
            Style = NormalizeMaskStyle(zone.Style, fallback.Style),
            Strength = MaskIntensityScale.Clamp(zone.Strength),
            Behavior = zone.Behavior,
            Enabled = zone.Enabled,
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
            ProcessNameMatchMode = matcher.ProcessNameMatchMode,
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

    private static List<PrivacyZone> MergeZones(
        IEnumerable<LayoutPreset> presets,
        string selectedPresetId,
        IEnumerable<PrivacyZone>? persistedZones,
        bool migrateLegacySurfaceIntensity)
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
                    Style = NormalizeMaskStyle(persisted.Style, defaultZone.Style),
                    Strength = migrateLegacySurfaceIntensity
                        ? MaskIntensityScale.FromLegacy(persisted.Strength)
                        : MaskIntensityScale.Clamp(persisted.Strength),
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

    private static MaskStyle NormalizeMaskStyle(MaskStyle persistedStyle, MaskStyle fallback)
    {
        return Enum.IsDefined(typeof(MaskStyle), persistedStyle)
            ? persistedStyle
            : fallback;
    }
}
