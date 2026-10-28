using System.IO;
using System.Runtime.InteropServices;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal static class RuntimeUpdatePreflight
{
    internal const string DisabledBuild = "update-build-not-enabled";
    internal const string UnsupportedSystem = "update-system-unsupported";
    internal const string UnsupportedArchitecture = "update-architecture-unsupported";
    internal const string OutsideManagedPath = "update-outside-managed-path";
    internal const string InstallationUnverified = "update-installation-unverified";

    // Fact inputs make admission order testable without touching real installations or HTTP.
    internal static UpdateCheckResult? Evaluate(bool configured, bool windows, int windowsBuild,
        Architecture architecture, string? processPath, string settingsPath, string root,
        Func<string, string?> resolveManagedLauncher)
    {
        if (!configured) return new(UpdateCheckStatus.NotConfigured, Reason: DisabledBuild);
        if (!windows || windowsBuild < UpdateRuntimePolicy.MinimumAutomaticWindowsBuild)
            return Manual(UnsupportedSystem);
        if (architecture != Architecture.X64) return Manual(UnsupportedArchitecture);
        try
        {
            if (processPath is null) return Manual(InstallationUnverified);
            if (!SamePath(processPath, Path.Combine(root, "app", LauncherBoot.ApplicationFileName)))
                return Manual(OutsideManagedPath);
            if (!SamePath(settingsPath, Path.Combine(root, "settings.v1.json")) || resolveManagedLauncher(processPath) is null)
                return Manual(InstallationUnverified);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or ReleaseValidationException or System.Security.SecurityException)
        {
            return Manual(InstallationUnverified);
        }
    }

    private static UpdateCheckResult Manual(string reason) => new(UpdateCheckStatus.ManualUpdateRequired, Reason: reason);
    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
