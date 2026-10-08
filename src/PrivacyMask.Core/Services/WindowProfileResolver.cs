using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using PrivacyMask.Core.Contracts;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public sealed class WindowProfileResolver
{
    private readonly IReadOnlyDictionary<AppId, IWindowAdapter> _adapters;
    private readonly ConditionalWeakTable<AppProfile, ProfileZoneCache> _zoneCaches = new();

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

    private IReadOnlyList<PrivacyZone> ResolveZones(AppProfile profile, LayoutPreset preset)
    {
        var sourceZones = profile.Zones.Count == 0 || profile.SelectedPresetId != preset.PresetId
            ? preset.Zones
            : profile.Zones;

        var profileCache = _zoneCaches.GetValue(profile, static _ => new ProfileZoneCache());
        if (profileCache.Entries.TryGetValue(preset.PresetId, out var cached)
            && cached.Matches(sourceZones, profile.MaskIntensity))
        {
            return cached.Zones;
        }

        var zones = sourceZones
            .Select(zone =>
            {
                var clone = PresetCatalog.CloneZone(zone);
                // The app-wide darkness slider is the final effective strength for the
                // current single-layer mask, so the runtime should not multiply it by
                // the preset baseline or it will saturate too early.
                clone.Strength = MaskIntensityScale.Clamp(profile.MaskIntensity);
                return clone;
            })
            .ToList();

        profileCache.Entries[preset.PresetId] = CachedZones.Create(sourceZones, profile.MaskIntensity, zones);
        return zones;
    }

    private sealed class ProfileZoneCache
    {
        public Dictionary<string, CachedZones> Entries { get; } = new(StringComparer.Ordinal);
    }

    private sealed class CachedZones
    {
        private readonly double _maskIntensity;
        private readonly ZoneConfiguration[] _configurations;

        private CachedZones(
            double maskIntensity,
            ZoneConfiguration[] configurations,
            IReadOnlyList<PrivacyZone> zones)
        {
            _maskIntensity = maskIntensity;
            _configurations = configurations;
            Zones = zones;
        }

        public IReadOnlyList<PrivacyZone> Zones { get; }

        public static CachedZones Create(
            IReadOnlyList<PrivacyZone> sourceZones,
            double maskIntensity,
            IReadOnlyList<PrivacyZone> effectiveZones)
        {
            var configurations = new ZoneConfiguration[sourceZones.Count];
            for (var index = 0; index < sourceZones.Count; index++)
            {
                configurations[index] = ZoneConfiguration.From(sourceZones[index]);
            }

            return new CachedZones(maskIntensity, configurations, effectiveZones);
        }

        public bool Matches(IReadOnlyList<PrivacyZone> sourceZones, double maskIntensity)
        {
            if (_maskIntensity != maskIntensity || _configurations.Length != sourceZones.Count)
            {
                return false;
            }

            for (var index = 0; index < sourceZones.Count; index++)
            {
                if (!_configurations[index].Matches(sourceZones[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private readonly record struct ZoneConfiguration(
        string ZoneId,
        string DisplayName,
        ZoneAnchor Anchor,
        double X,
        double Y,
        double Width,
        double Height,
        MaskStyle Style,
        double Strength,
        ZoneBehavior Behavior,
        bool Enabled)
    {
        public static ZoneConfiguration From(PrivacyZone zone)
        {
            return new ZoneConfiguration(
                zone.ZoneId,
                zone.DisplayName,
                zone.Anchor,
                zone.RelativeRect.X,
                zone.RelativeRect.Y,
                zone.RelativeRect.Width,
                zone.RelativeRect.Height,
                zone.Style,
                zone.Strength,
                zone.Behavior,
                zone.Enabled);
        }

        public bool Matches(PrivacyZone zone)
        {
            return string.Equals(ZoneId, zone.ZoneId, StringComparison.Ordinal)
                && string.Equals(DisplayName, zone.DisplayName, StringComparison.Ordinal)
                && Anchor == zone.Anchor
                && X == zone.RelativeRect.X
                && Y == zone.RelativeRect.Y
                && Width == zone.RelativeRect.Width
                && Height == zone.RelativeRect.Height
                && Style == zone.Style
                && Strength == zone.Strength
                && Behavior == zone.Behavior
                && Enabled == zone.Enabled;
        }
    }
}
