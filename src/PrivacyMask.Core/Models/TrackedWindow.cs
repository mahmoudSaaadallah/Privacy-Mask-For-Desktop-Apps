using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class TrackedWindow
{
    public required AppProfile Profile { get; init; }

    public required LayoutPreset Preset { get; init; }

    public required WindowSnapshot Snapshot { get; init; }

    public required IReadOnlyList<PrivacyZone> EffectiveZones { get; init; }
}
