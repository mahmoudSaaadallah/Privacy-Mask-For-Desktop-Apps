using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class ProfileRuntimeStatusResolver
{
    public static ProfileRuntimeStatus Resolve(
        AppProfile profile,
        IReadOnlySet<string> runningProcessNames,
        IReadOnlySet<string> protectedProfileIds,
        RuntimeMode globalMode,
        bool profilePaused,
        bool profilePanic,
        bool executableExists)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(runningProcessNames);
        ArgumentNullException.ThrowIfNull(protectedProfileIds);

        if (!profile.Enabled)
        {
            return ProfileRuntimeStatus.Disabled;
        }

        var isRunning = profile.WindowMatchers
            .SelectMany(matcher => matcher.ProcessNames.Select(processName => (matcher, processName)))
            .Any(entry => runningProcessNames.Any(actual =>
                entry.matcher.ProcessNameMatchMode == ProcessNameMatchMode.Exact
                    ? string.Equals(entry.processName, actual, StringComparison.OrdinalIgnoreCase)
                    : ProcessNameMatcher.Matches(entry.processName, actual)));
        if (!isRunning)
        {
            return profile.AppId == AppId.Custom
                && !string.IsNullOrWhiteSpace(profile.ExecutablePath)
                && !executableExists
                    ? ProfileRuntimeStatus.TargetMissing
                    : ProfileRuntimeStatus.NotRunning;
        }

        if (globalMode == RuntimeMode.Off || profilePaused)
        {
            return ProfileRuntimeStatus.ProtectionPaused;
        }

        if (globalMode == RuntimeMode.Panic || profilePanic)
        {
            return ProfileRuntimeStatus.PanicProtected;
        }

        return protectedProfileIds.Contains(profile.ProfileId)
            ? ProfileRuntimeStatus.Protected
            : ProfileRuntimeStatus.WaitingForFocus;
    }
}
