using System.Collections.Generic;
using System.Linq;
using PrivacyMask.Core.Contracts;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public sealed class WindowProfileResolver
{
    private readonly IReadOnlyDictionary<AppId, IWindowAdapter> _adapters;

    public WindowProfileResolver(IEnumerable<IWindowAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(adapter => adapter.AppId);
    }

    public TrackedWindow? Resolve(WindowSnapshot snapshot, IReadOnlyList<AppProfile> profiles)
    {
        foreach (var profile in profiles.Where(candidate => candidate.Enabled))
        {
            if (!_adapters.TryGetValue(profile.AppId, out var adapter))
            {
                continue;
            }

            if (!adapter.IsMatch(snapshot, profile))
            {
                continue;
            }

            var preset = adapter.SelectPreset(snapshot, profile, profile.Presets);
            var effectiveZones = ResolveZones(profile, preset);

            return new TrackedWindow
            {
                Profile = profile,
                Preset = preset,
                Snapshot = snapshot,
                EffectiveZones = effectiveZones,
            };
        }

        return null;
    }

    private static IReadOnlyList<PrivacyZone> ResolveZones(AppProfile profile, LayoutPreset preset)
    {
        var sourceZones = profile.Zones.Count == 0 || profile.SelectedPresetId != preset.PresetId
            ? preset.Zones
            : profile.Zones;

        return sourceZones
            .Select(zone =>
            {
                var clone = PresetCatalog.CloneZone(zone);
                // The app-wide darkness slider is the final effective strength for the
                // current single-layer mask, so the runtime should not multiply it by
                // the preset baseline or it will saturate too early.
                clone.Strength = double.Clamp(profile.MaskIntensity, 0.60d, 2.40d);
                return clone;
            })
            .ToList();
    }
}
