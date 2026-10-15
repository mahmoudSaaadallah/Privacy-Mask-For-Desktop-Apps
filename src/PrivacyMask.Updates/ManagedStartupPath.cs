namespace PrivacyMask.Updates;

internal static class ManagedStartupPath
{
    internal static string? Resolve(string applicationPath)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(applicationPath)) return null;
        var app = Path.GetFullPath(applicationPath);
        var appDirectory = Path.GetDirectoryName(app);
        var root = appDirectory is null ? null : Path.GetDirectoryName(appDirectory);
        if (appDirectory is null || root is null || Path.GetFileName(appDirectory) != "app" || Path.GetFileName(root) != ManifestRules.AppId ||
            !string.Equals(app, Path.Combine(root, "app", LauncherBoot.ApplicationFileName), StringComparison.OrdinalIgnoreCase))
            return null;

        var launcher = Path.Combine(root, LauncherBoot.LauncherFileName);
        var markerPath = Path.Combine(root, "managed-install.json");
        ManagedInstallLease.AssertPrivate(root);
        ManagedInstallLease.AssertPrivate(appDirectory);
        ManagedInstallLease.AssertPrivate(app);
        ManagedInstallLease.AssertPrivate(launcher);
        ManagedInstallLease.AssertPrivate(markerPath);
        var marker = RecoveryJson.Read(ManagedInstallLease.ReadBounded(markerPath, 4096), 4096,
            RecoveryJson.Context.ManagedInstallMarker);
        RecoveryRules.Validate(marker);
        return launcher;
    }
}
