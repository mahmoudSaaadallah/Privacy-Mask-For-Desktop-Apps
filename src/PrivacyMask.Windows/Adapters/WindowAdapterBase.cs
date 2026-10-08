using System;
using System.Collections.Generic;
using System.Linq;
using PrivacyMask.Core.Contracts;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Windows.Adapters;

public abstract class WindowAdapterBase : IWindowAdapter
{
    public abstract AppId AppId { get; }

    public bool IsMatch(WindowSnapshot snapshot, AppProfile profile)
    {
        if (snapshot.IsMinimized || !snapshot.IsVisible)
        {
            return false;
        }

        return profile.WindowMatchers.Any(matcher => Matches(matcher, snapshot));
    }

    public LayoutPreset SelectPreset(WindowSnapshot snapshot, AppProfile profile, IReadOnlyList<LayoutPreset> presets)
    {
        if (presets.Count > 0 && presets.All(preset => preset.AppId == AppId))
        {
            return SelectPresetCore(snapshot, presets);
        }

        var supportedPresets = presets.Where(preset => preset.AppId == AppId).ToList();
        if (supportedPresets.Count == 0)
        {
            throw new InvalidOperationException($"No presets configured for {AppId}.");
        }

        return SelectPresetCore(snapshot, supportedPresets);
    }

    protected abstract LayoutPreset SelectPresetCore(WindowSnapshot snapshot, IReadOnlyList<LayoutPreset> presets);

    private static bool Matches(WindowMatcher matcher, WindowSnapshot snapshot)
    {
        var processMatch = matcher.ProcessNames.Count == 0
            || matcher.ProcessNames.Any(candidate => matcher.ProcessNameMatchMode == ProcessNameMatchMode.Exact
                ? string.Equals(candidate, snapshot.ProcessName, StringComparison.OrdinalIgnoreCase)
                : ProcessNameMatcher.Matches(candidate, snapshot.ProcessName));

        if (!processMatch)
        {
            return false;
        }

        var titleMatch = string.IsNullOrWhiteSpace(matcher.TitleContains)
            || snapshot.Title.Contains(matcher.TitleContains, StringComparison.OrdinalIgnoreCase);

        if (!titleMatch)
        {
            return false;
        }

        var classMatch = string.IsNullOrWhiteSpace(matcher.ClassNameContains)
            || snapshot.ClassName.Contains(matcher.ClassNameContains, StringComparison.OrdinalIgnoreCase);

        return classMatch;
    }
}
