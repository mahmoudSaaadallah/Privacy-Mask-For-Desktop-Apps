using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.App.Services;

internal static class TaskManagerPanicMask
{
    internal const string ProcessName = "Taskmgr";
    private static readonly AppProfile RuntimeProfile = CustomAppProfileFactory.Create("Task Manager", ProcessName);

    internal static TrackedWindow? TryCreate(WindowSnapshot window, bool panicActive)
    {
        if (!panicActive || !window.IsVisible || window.IsMinimized
            || !string.Equals(window.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase))
            return null;

        return new TrackedWindow
        {
            Snapshot = window,
            Profile = RuntimeProfile,
            Preset = RuntimeProfile.Presets[0],
            EffectiveZones = [],
        };
    }

    internal static bool IsAutomaticMask(TrackedWindow window) => ReferenceEquals(window.Profile, RuntimeProfile);
}
