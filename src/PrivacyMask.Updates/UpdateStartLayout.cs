namespace PrivacyMask.Updates;

internal sealed record UpdateSettingsBaseline(
    string PrimaryPath,
    string BackupPath,
    string OriginalPrimaryPath,
    string OriginalBackupPath,
    RecoveryFile Primary,
    RecoveryFile Backup);

internal sealed record UpdateStartLayout(
    string Root,
    string ApplicationPath,
    string LauncherPath,
    string JournalPath,
    RecoveryJournal Journal,
    UpdateSettingsBaseline Settings)
{
    internal static UpdateStartLayout Open(UpdateStartRequest request)
    {
        var executable = Environment.ProcessPath ?? throw new ReleaseValidationException("Missing application image path.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ManifestRules.AppId);
        return Open(request, executable, root);
    }

    internal static UpdateStartLayout Open(UpdateStartRequest request, string runningExecutablePath, string expectedRoot)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(runningExecutablePath) || !Path.IsPathFullyQualified(expectedRoot))
            throw Invalid();

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedRoot));
        var applicationPath = Path.GetFullPath(runningExecutablePath);
        var appDirectory = Path.Combine(root, "app");
        var expectedApplication = Path.Combine(appDirectory, "PrivacyMask.App.exe");
        var launcherPath = Path.Combine(root, "PrivacyMask.Launcher.exe");
        var markerPath = Path.Combine(root, "managed-install.json");
        var journalPath = Path.Combine(root, "update-journal.json");
        var lockPath = Path.Combine(root, ".update.lock");

        if (Path.GetFileName(root) != ManifestRules.AppId ||
            !string.Equals(applicationPath, expectedApplication, StringComparison.OrdinalIgnoreCase))
            throw Invalid();

        AssertPrivatePath(root);
        AssertPrivatePath(appDirectory);
        var appEntries = Directory.EnumerateFileSystemEntries(appDirectory).Take(2).ToArray();
        if (appEntries.Length != 1 || !string.Equals(Path.GetFullPath(appEntries[0]), applicationPath, StringComparison.OrdinalIgnoreCase))
            throw Invalid();

        AssertPrivatePath(applicationPath);
        AssertPrivatePath(launcherPath);
        AssertPrivatePath(markerPath);
        AssertPrivatePath(journalPath);
        AssertPrivatePath(lockPath);
        if (RecoveryRestorer.Inspect(launcherPath, ManifestRules.MaximumFileSize) is not { Exists: true, Size: > 0 })
            throw Invalid();

        var marker = RecoveryJson.Read(ManagedInstallLease.ReadBounded(markerPath, 4096), 4096,
            RecoveryJson.Context.ManagedInstallMarker);
        RecoveryRules.Validate(marker);
        if (marker.InstallId != request.InstallId)
            throw Invalid();

        var journal = ReadJournal(journalPath);
        if (journal.InstallId != request.InstallId || journal.OperationId != request.OperationId ||
            journal.Step != RecoveryStep.AwaitingReadiness ||
            RecoveryRestorer.Inspect(applicationPath, ManifestRules.MaximumFileSize) != journal.TargetApp)
            throw Invalid();

        var operations = Path.Combine(root, "update-operations");
        var operation = Path.Combine(operations, "operation-" + request.OperationId);
        AssertPrivatePath(operations);
        AssertPrivatePath(operation);

        var primaryPath = Path.Combine(root, "settings.v1.json");
        var backupPath = primaryPath + ".bak";
        var originalPrimaryPath = Path.Combine(operation, "original-settings.json");
        var originalBackupPath = Path.Combine(operation, "original-settings.bak");
        AssertSnapshot(primaryPath, journal.SettingsPrimary, RecoveryRules.SettingsLimit);
        AssertSnapshot(backupPath, journal.SettingsBackup, RecoveryRules.SettingsLimit);
        AssertSnapshot(originalPrimaryPath, journal.SettingsPrimary, RecoveryRules.SettingsLimit);
        AssertSnapshot(originalBackupPath, journal.SettingsBackup, RecoveryRules.SettingsLimit);
        AssertSnapshot(Path.Combine(operation, "original-app.exe"), journal.OriginalApp, ManifestRules.MaximumFileSize);
        AssertSnapshot(Path.Combine(operation, "displaced-original-app.exe"), journal.OriginalApp, ManifestRules.MaximumFileSize);

        return new(root, applicationPath, launcherPath, journalPath, journal,
            new(primaryPath, backupPath, originalPrimaryPath, originalBackupPath,
                journal.SettingsPrimary, journal.SettingsBackup));
    }

    internal RecoveryJournal ReadCurrentJournal()
    {
        var current = ReadJournal(JournalPath);
        if (current.InstallId != Journal.InstallId || current.OperationId != Journal.OperationId)
            throw Invalid();
        return current;
    }

    internal void AssertApplicationUnchanged()
    {
        if (RecoveryRestorer.Inspect(ApplicationPath, ManifestRules.MaximumFileSize) != Journal.TargetApp)
            throw Invalid();
    }

    private static RecoveryJournal ReadJournal(string path)
    {
        AssertPrivatePath(path);
        var journal = RecoveryJson.Read(ManagedInstallLease.ReadBounded(path, RecoveryRules.JournalLimit),
            RecoveryRules.JournalLimit, RecoveryJson.Context.RecoveryJournal);
        RecoveryRules.Validate(journal);
        return journal;
    }

    private static void AssertSnapshot(string path, RecoveryFile expected, long maximum)
    {
        if (RecoveryRestorer.Inspect(path, maximum) != expected)
            throw Invalid();
    }

    private static void AssertPrivatePath(string path)
    {
        StagingWorkspace.AssertSafe(path);
        ManagedInstallLease.AssertPrivate(path);
    }

    private static ReleaseValidationException Invalid() =>
        new("Authenticated update-start layout validation failed.");
}
