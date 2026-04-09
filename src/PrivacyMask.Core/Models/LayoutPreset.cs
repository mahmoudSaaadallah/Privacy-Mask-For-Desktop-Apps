using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class LayoutPreset
{
    public string PresetId { get; init; } = string.Empty;

    public AppId AppId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string LayoutVariant { get; init; } = string.Empty;

    public double MinWindowWidth { get; init; }

    public double MinWindowHeight { get; init; }

    public List<PrivacyZone> Zones { get; init; } = [];
}
