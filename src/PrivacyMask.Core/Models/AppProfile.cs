using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class AppProfile
{
    public AppId AppId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public AppActivationMode StartupMode { get; set; } = AppActivationMode.Manual;

    public double MaskIntensity { get; set; } = 1.35d;

    public int HoverRevealWidthPixels { get; set; } = 394;

    public int HoverRevealHeightPixels { get; set; } = 42;

    public List<WindowMatcher> WindowMatchers { get; set; } = [];

    public List<PrivacyZone> Zones { get; set; } = [];

    public List<HotkeyBinding> Hotkeys { get; set; } = [];

    public List<LayoutPreset> Presets { get; init; } = [];

    public string SelectedPresetId { get; set; } = string.Empty;
}
