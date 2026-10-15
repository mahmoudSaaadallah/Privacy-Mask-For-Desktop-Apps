using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrivacyMask.Updates;

internal enum RestoreCheckpoint { IntentSaved, FilePreserved, FileRestored, RecoverySaved }

/// <summary>
/// Rollback kernel only. Caller MUST own graceful process exit and frozen settings writers.
/// No process launch, stop, migration, readiness, consent or download authorization here.
/// </summary>
internal sealed class RecoveryRestorer(ManagedInstallLease lease, Action<RestoreCheckpoint, string>? checkpoint = null)
{
    internal RecoveryResult Recover()
    {
        lease.AssertHeld();
        try
        {
            var store = new RecoveryJournalStore(lease);
            var journal = store.Read();
            if (journal is null) return new(RecoveryOutcome.NoPendingOperation);
            var operation = lease.OperationDirectory(journal.OperationId);
            var appDirectory = Path.Combine(lease.Root, "app");
            ManagedInstallLease.AssertPrivate(appDirectory);
            if (Directory.EnumerateFileSystemEntries(appDirectory).Take(2).Any(path => Path.GetFileName(path) != "PrivacyMask.App.exe"))
                return Repair("unexpected-app-directory-content");
            var app = Path.Combine(appDirectory, "PrivacyMask.App.exe");
            var primary = Path.Combine(lease.Root, "settings.v1.json");
            var secondary = Path.Combine(lease.Root, "settings.v1.json.bak");
            if (journal.Step == RecoveryStep.Committed)
                return Matches(app, journal.TargetApp, ManifestRules.MaximumFileSize)
                    ? new(RecoveryOutcome.Committed) : Repair("committed-payload-missing-or-changed");
            if (journal.Step == RecoveryStep.Recovered)
                // Once durable recovery is recorded, later normal settings edits must not be undone.
                return Matches(app, journal.OriginalApp, ManifestRules.MaximumFileSize)
                    ? new(RecoveryOutcome.Recovered) : Repair("recovered-baseline-changed");

            // Validate EVERY backup before moving even one installed file. No partial baseline guessing.
            var appBackup = Path.Combine(operation, "original-app.exe");
            var primaryBackup = Path.Combine(operation, "original-settings.json");
            var secondaryBackup = Path.Combine(operation, "original-settings.bak");
            if (!Matches(appBackup, journal.OriginalApp, ManifestRules.MaximumFileSize) ||
                !Matches(primaryBackup, journal.SettingsPrimary, RecoveryRules.SettingsLimit) ||
                !Matches(secondaryBackup, journal.SettingsBackup, RecoveryRules.SettingsLimit)) return Repair("recovery-backup-missing-or-changed");
            var actualApp = Inspect(app, ManifestRules.MaximumFileSize);
            if (actualApp.Exists && actualApp != journal.OriginalApp && actualApp != journal.TargetApp)
                return Repair("unknown-installed-payload");
            // Also validate settings/path/size and evidence collisions before any journal/file mutation.
            Preflight(app, "app", journal.OriginalApp, ManifestRules.MaximumFileSize);
            Preflight(primary, "primary", journal.SettingsPrimary, RecoveryRules.SettingsLimit);
            Preflight(secondary, "secondary", journal.SettingsBackup, RecoveryRules.SettingsLimit);
            if (journal.Step != RecoveryStep.RollingBack)
            {
                journal = journal with { Step = RecoveryStep.RollingBack, Revision = journal.Revision + 1 };
                store.Write(journal);
            }
            checkpoint?.Invoke(RestoreCheckpoint.IntentSaved, "journal");
            Restore(app, appBackup, "app", journal.OriginalApp, ManifestRules.MaximumFileSize);
            Restore(primary, primaryBackup, "primary", journal.SettingsPrimary, RecoveryRules.SettingsLimit);
            Restore(secondary, secondaryBackup, "secondary", journal.SettingsBackup, RecoveryRules.SettingsLimit);
            journal = journal with { Step = RecoveryStep.Recovered, Revision = journal.Revision + 1 };
            store.Write(journal);
            checkpoint?.Invoke(RestoreCheckpoint.RecoverySaved, "journal");
            return new(RecoveryOutcome.Recovered);

            void Preflight(string target, string label, RecoveryFile baseline, long bound)
            {
                var actual = Inspect(target, bound);
                var evidence = Path.Combine(operation, "failed-" + label);
                StagingWorkspace.AssertSafe(evidence);
                // If both exist and differ, don't overwrite prior evidence or discard the installed file.
                if (actual != baseline && actual.Exists && Inspect(evidence, bound).Exists)
                    throw new ReleaseValidationException("Conflicting recovery evidence.");
            }

            void Restore(string target, string backup, string label, RecoveryFile baseline, long bound)
            {
                lease.AssertHeld();
                _ = lease.OperationDirectory(journal.OperationId);
                if (Matches(target, baseline, bound)) return;
                string? scratch = null;
                if (baseline.Exists)
                {
                    scratch = Path.Combine(operation, "restore-" + label + "-" + Guid.NewGuid().ToString("N") + ".tmp");
                    // Copy from a verified retained baseline; never consume the last good backup.
                    using var input = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using (var output = new FileStream(scratch, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
                    {
                        if (baseline.Size == 0)
                        {
                            if (input.ReadByte() != -1 || Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>())) != baseline.Sha256)
                                throw new ReleaseValidationException("Empty snapshot changed.");
                        }
                        else UpdatePayload.CopyVerifiedAsync(input, output, baseline.Size, baseline.Sha256!).GetAwaiter().GetResult();
                        output.Flush(flushToDisk: true);
                    }
                    ManagedInstallLease.AssertPrivate(scratch);
                }
                StagingWorkspace.AssertSafe(target);
                var current = Inspect(target, bound);
                if (label == "app" && current.Exists && current != journal.OriginalApp && current != journal.TargetApp)
                    throw new ReleaseValidationException("Installed payload changed during recovery.");
                if (current.Exists)
                {
                    var evidence = Path.Combine(operation, "failed-" + label);
                    StagingWorkspace.AssertSafe(evidence);
                    File.Move(target, evidence, overwrite: false);
                    checkpoint?.Invoke(RestoreCheckpoint.FilePreserved, label);
                }
                if (scratch is not null) File.Move(scratch, target, overwrite: false);
                checkpoint?.Invoke(RestoreCheckpoint.FileRestored, label);
                if (!Matches(target, baseline, bound)) throw new ReleaseValidationException("Restored baseline changed.");
            }
        }
        catch (Exception exception) when (exception is ReleaseValidationException or JsonException or DecoderFallbackException)
        { return Repair("invalid-recovery-state"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return Repair("recovery-io-failed"); }
    }

    private static RecoveryResult Repair(string reason) => new(RecoveryOutcome.RepairRequired, reason);
    private static bool Matches(string path, RecoveryFile expected, long maximum) => Inspect(path, maximum) == expected;

    internal static RecoveryFile Inspect(string path, long maximum)
    {
        StagingWorkspace.AssertSafe(path);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return new(false, 0, null); }
        if ((attributes & FileAttributes.Directory) != 0) throw new ReleaseValidationException("Recovery file is a directory.");
        ManagedInstallLease.AssertPrivate(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximum) throw new ReleaseValidationException("Recovery file exceeds its size bound.");
        var size = file.Length;
        var hash = Convert.ToHexStringLower(SHA256.HashData(file));
        if (file.Length != size) throw new ReleaseValidationException("Recovery file changed during reading.");
        return new(true, size, hash);
    }
}
