using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class AppProfile
{
    public string ProfileId { get; init; } = string.Empty;

    public AppId AppId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public AppActivationMode StartupMode { get; set; } = AppActivationMode.Manual;

    public double MaskIntensity { get; set; } = MaskIntensityScale.Default;

    public MaskColorOption MaskColor { get; set; } = MaskColorOption.Black;

    public int HoverRevealWidthPixels { get; set; } = 600;

    public int HoverRevealHeightPixels { get; set; } = 70;

    public List<WindowMatcher> WindowMatchers { get; set; } = [];

    public List<PrivacyZone> Zones { get; set; } = [];

    public List<HotkeyBinding> Hotkeys { get; set; } = [];

    public List<LayoutPreset> Presets { get; init; } = [];

    public string SelectedPresetId { get; set; } = string.Empty;
}
