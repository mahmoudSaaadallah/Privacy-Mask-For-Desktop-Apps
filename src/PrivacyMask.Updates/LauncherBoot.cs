using System.Diagnostics;

namespace PrivacyMask.Updates;

internal static class LauncherBoot
{
    internal const string LauncherFileName = "PrivacyMask.Launcher.exe";
    internal const string ApplicationFileName = "PrivacyMask.App.exe";

    internal static Process Launch(string launcherPath, IReadOnlyList<string> arguments,
        Func<ProcessStartInfo, Process?>? start = null, Action<string>? inspectApplication = null)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(launcherPath) ||
            arguments.Count > 1 || (arguments.Count == 1 && arguments[0] != "--minimized"))
            throw new ReleaseValidationException("Unsupported launcher invocation.");

        var self = Path.GetFullPath(launcherPath);
        var root = Path.GetDirectoryName(self) ?? throw new ReleaseValidationException("Missing launcher root.");
        if (Path.GetFileName(self) != LauncherFileName || Path.GetFileName(root) != ManifestRules.AppId ||
            !string.Equals(self, Path.Combine(root, LauncherFileName), StringComparison.OrdinalIgnoreCase))
            throw new ReleaseValidationException("Launcher is outside the managed installation.");
        ManagedInstallLease.AssertPrivate(self);

        using var lease = ManagedInstallLease.Open(root);
        var recovery = new RecoveryRestorer(lease).Recover();
        if (recovery.Outcome == RecoveryOutcome.RepairRequired)
            throw new ReleaseValidationException("Managed installation requires repair.");
        if (recovery.Outcome is RecoveryOutcome.Committed or RecoveryOutcome.Recovered)
        {
            var terminal = new RecoveryJournalStore(lease).Read()
                ?? throw new ReleaseValidationException("Terminal recovery journal is missing.");
            new RecoveryJournalStore(lease).ArchiveTerminal(terminal.OperationId);
        }

        var appDirectory = Path.Combine(root, "app");
        ManagedInstallLease.AssertPrivate(appDirectory);
        var entries = Directory.EnumerateFileSystemEntries(appDirectory).Take(2).ToArray();
        var applicationPath = Path.Combine(appDirectory, ApplicationFileName);
        if (entries.Length != 1 || !string.Equals(Path.GetFullPath(entries[0]), applicationPath, StringComparison.OrdinalIgnoreCase) ||
            RecoveryRestorer.Inspect(applicationPath, ManifestRules.MaximumFileSize) is not { Exists: true, Size: > 0 })
            throw new ReleaseValidationException("Managed application payload is invalid.");

        (inspectApplication ?? InspectApplication)(applicationPath);
        lease.AssertHeld();
        var processStart = new ProcessStartInfo(applicationPath)
        {
            UseShellExecute = false,
            WorkingDirectory = root
        };
        if (arguments.Count == 1) processStart.ArgumentList.Add(arguments[0]);
        var child = (start ?? Process.Start)(processStart)
            ?? throw new IOException("PrivacyMask application did not start.");
        return child;
    }

    private static void InspectApplication(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        var productVersion = version.ProductVersion?.Split('+')[0];
        if (version.ProductName != "PrivacyMask for Desktop Apps" || productVersion is null ||
            !ManifestRules.IsVersion(productVersion))
            throw new ReleaseValidationException("Unexpected managed application identity.");
    }
}
