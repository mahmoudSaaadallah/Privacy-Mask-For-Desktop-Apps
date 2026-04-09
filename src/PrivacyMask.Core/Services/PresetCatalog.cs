using System.Collections.Generic;
using System.Linq;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class PresetCatalog
{
    public static IReadOnlyList<LayoutPreset> GetDefaultPresets(AppId appId)
    {
        return appId switch
        {
            AppId.WhatsApp => BuildWhatsAppPresets(),
            AppId.Telegram => BuildTelegramPresets(),
            _ => [],
        };
    }

    private static IReadOnlyList<LayoutPreset> BuildWhatsAppPresets()
    {
        return
        [
            new LayoutPreset
            {
                PresetId = "whatsapp-wide",
                AppId = AppId.WhatsApp,
                DisplayName = "WhatsApp wide layout",
                LayoutVariant = "wide",
                MinWindowWidth = 900,
                MinWindowHeight = 620,
                Zones =
                [
                    Zone("full-window", "Full app mask", ZoneAnchor.Window, new RelativeRect(0.00d, 0.00d, 1.00d, 1.00d), MaskStyle.Blur, 1.35d, ZoneBehavior.RevealOnHover | ZoneBehavior.HideDuringTemporaryReveal),
                ],
            },
            new LayoutPreset
            {
                PresetId = "whatsapp-compact",
                AppId = AppId.WhatsApp,
                DisplayName = "WhatsApp compact layout",
                LayoutVariant = "compact",
                MinWindowWidth = 520,
                MinWindowHeight = 520,
                Zones =
                [
                    Zone("full-window", "Full app mask", ZoneAnchor.Window, new RelativeRect(0.00d, 0.00d, 1.00d, 1.00d), MaskStyle.Blur, 1.35d, ZoneBehavior.RevealOnHover | ZoneBehavior.HideDuringTemporaryReveal),
                ],
            },
        ];
    }

    private static IReadOnlyList<LayoutPreset> BuildTelegramPresets()
    {
        return
        [
            new LayoutPreset
            {
                PresetId = "telegram-wide",
                AppId = AppId.Telegram,
                DisplayName = "Telegram wide layout",
                LayoutVariant = "wide",
                MinWindowWidth = 920,
                MinWindowHeight = 620,
                Zones =
                [
                    Zone("full-window", "Full app mask", ZoneAnchor.Window, new RelativeRect(0.00d, 0.00d, 1.00d, 1.00d), MaskStyle.Blur, 1.35d, ZoneBehavior.RevealOnHover | ZoneBehavior.HideDuringTemporaryReveal),
                ],
            },
            new LayoutPreset
            {
                PresetId = "telegram-compact",
                AppId = AppId.Telegram,
                DisplayName = "Telegram compact layout",
                LayoutVariant = "compact",
                MinWindowWidth = 520,
                MinWindowHeight = 520,
                Zones =
                [
                    Zone("full-window", "Full app mask", ZoneAnchor.Window, new RelativeRect(0.00d, 0.00d, 1.00d, 1.00d), MaskStyle.Blur, 1.35d, ZoneBehavior.RevealOnHover | ZoneBehavior.HideDuringTemporaryReveal),
                ],
            },
        ];
    }

    private static PrivacyZone Zone(
        string zoneId,
        string displayName,
        ZoneAnchor anchor,
        RelativeRect relativeRect,
        MaskStyle style,
        double strength,
        ZoneBehavior behavior)
    {
        return new PrivacyZone
        {
            ZoneId = zoneId,
            DisplayName = displayName,
            Anchor = anchor,
            RelativeRect = relativeRect.Clamp(),
            Style = style,
            Strength = strength,
            Behavior = behavior,
            Enabled = true,
        };
    }

    public static IReadOnlyList<LayoutPreset> ClonePresets(IEnumerable<LayoutPreset> presets)
    {
        return presets.Select(ClonePreset).ToList();
    }

    public static LayoutPreset ClonePreset(LayoutPreset preset)
    {
        return new LayoutPreset
        {
            PresetId = preset.PresetId,
            AppId = preset.AppId,
            DisplayName = preset.DisplayName,
            LayoutVariant = preset.LayoutVariant,
            MinWindowWidth = preset.MinWindowWidth,
            MinWindowHeight = preset.MinWindowHeight,
            Zones = preset.Zones.Select(CloneZone).ToList(),
        };
    }

    public static PrivacyZone CloneZone(PrivacyZone zone)
    {
        return new PrivacyZone
        {
            ZoneId = zone.ZoneId,
            DisplayName = zone.DisplayName,
            Anchor = zone.Anchor,
            RelativeRect = zone.RelativeRect with { },
            Style = zone.Style,
            Strength = zone.Strength,
            Behavior = zone.Behavior,
            Enabled = zone.Enabled,
        };
    }
}
