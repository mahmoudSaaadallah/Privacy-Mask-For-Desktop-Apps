using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrivacyMask.Updates;

/// <summary>Lease-scoped, bounded maintenance. Only terminal history or expired staging can enter quarantine.
/// No recursive deletion, restore, network, live-settings mutation or search outside fixed managed descendants.</summary>
internal sealed class UpdateRetention(ManagedInstallLease lease, TimeProvider? clock = null,
    Action<RetentionCheckpoint>? checkpoint = null)
{
    private sealed record Terminal(string Path, string Id, RecoveryJournal Journal, DateTime ArchivedAtUtc);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    internal async Task<RetentionResult> RunAsync(string? protectedStageId = null, CancellationToken cancellationToken = default)
    {
        if (protectedStageId is not null && !RecoveryRules.IsId(protectedStageId))
            throw new ReleaseValidationException("Invalid protected staging identity.");
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var removed = 0;
        long bytes = 0;
        var partial = false;
        var pins = new List<PreparationFile>();
        try
        {
            lease.AssertHeld();
            // A malformed journal also prevents all maintenance through the fail-closed catch below.
            if (new RecoveryJournalStore(lease).Read() is not null) return new(RetentionOutcome.Deferred);
            // Finish bounded catalog scans before authorizing any mutation.
            var operations = UpdateStoragePolicy.Catalog(lease, "update-operations");
            var stages = UpdateStoragePolicy.Catalog(lease, "update-staging");
            var quarantined = UpdateStoragePolicy.Catalog(lease, "update-pruning");
            var terminals = new List<Terminal>();
            foreach (var path in operations)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!UpdateStoragePolicy.TryId(path, "operation-", out var id)) { partial = true; continue; }
                    AssertDirectory(path);
                    var archive = Path.Combine(path, "terminal-journal.json");
                    if (!File.Exists(archive)) { partial = true; continue; } // Never age out unresolved backups.
                    var terminal = ReadTerminal(path, id);
                    _ = Files(path, staging: false);
                    terminals.Add(terminal);
                }
                catch (Exception error) when (IsPreservable(error)) { partial = true; }
            }

            string? retainedId = null;
            if (terminals.Count > 0)
            {
                var installed = await PreparationFile.OpenAsync(Path.Combine(lease.Root, "app", LauncherBoot.ApplicationFileName),
                    ManifestRules.MaximumFileSize, true, token).ConfigureAwait(false);
                pins.Add(installed);
                // Prefer a successful update's predecessor. A recovered baseline is the conservative fallback.
                foreach (var terminal in terminals
                    .OrderByDescending(item => item.Journal.Step == RecoveryStep.Committed)
                    .ThenByDescending(item => ParseVersion(item.Journal.TargetVersion))
                    .ThenByDescending(item => item.ArchivedAtUtc)
                    .ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    var expected = terminal.Journal.Step == RecoveryStep.Committed ? terminal.Journal.TargetApp : terminal.Journal.OriginalApp;
                    if (expected != installed.Snapshot) continue;
                    var candidatePins = new List<PreparationFile>();
                    try
                    {
                        foreach (var (name, snapshot, limit) in new[] {
                            ("original-app.exe", terminal.Journal.OriginalApp, ManifestRules.MaximumFileSize),
                            ("original-settings.json", terminal.Journal.SettingsPrimary, RecoveryRules.SettingsLimit),
                            ("original-settings.bak", terminal.Journal.SettingsBackup, RecoveryRules.SettingsLimit) })
                        {
                            var pin = await PreparationFile.OpenAsync(Path.Combine(terminal.Path, name), limit,
                                name == "original-app.exe", token).ConfigureAwait(false);
                            candidatePins.Add(pin);
                            if (pin.Snapshot != snapshot) throw new ReleaseValidationException("Retained recovery baseline changed.");
                        }
                        retainedId = terminal.Id;
                        pins.AddRange(candidatePins);
                        break;
                    }
                    catch (Exception error) when (IsPreservable(error))
                    {
                        foreach (var pin in candidatePins) pin.Dispose();
                        partial = true;
                    }
                    catch { foreach (var pin in candidatePins) pin.Dispose(); throw; }
                }
                if (retainedId is null) partial = true; // No proven replacement backup: preserve all terminal histories.
            }

            foreach (var path in quarantined.Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (removed >= UpdateStoragePolicy.MaximumRemovalsPerRun) { partial = true; break; }
                if (UpdateStoragePolicy.TryId(path, "operation-", out _) && retainedId is null)
                { partial = true; continue; }
                try { RemoveQuarantined(path); }
                catch (Exception error) when (IsPreservable(error)) { partial = true; }
            }

            if (retainedId is not null)
                foreach (var terminal in terminals.Where(item => item.Id != retainedId).OrderBy(item => ParseVersion(item.Journal.TargetVersion)))
                {
                    token.ThrowIfCancellationRequested();
                    if (removed >= UpdateStoragePolicy.MaximumRemovalsPerRun) { partial = true; break; }
                    try { Quarantine(terminal.Path, "operation-" + terminal.Id, staging: false); }
                    catch (Exception error) when (IsPreservable(error)) { partial = true; }
                }

            foreach (var stage in stages.Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (removed >= UpdateStoragePolicy.MaximumRemovalsPerRun) { partial = true; break; }
                try
                {
                    if (!UpdateStoragePolicy.TryId(stage, "operation-", out var id)) { partial = true; continue; }
                    if (id == protectedStageId) continue;
                    var files = Files(stage, staging: true);
                    var now = _clock.GetUtcNow().UtcDateTime;
                    var cutoff = now - UpdateStoragePolicy.StageLifetime;
                    if (new[] { Directory.GetCreationTimeUtc(stage), Directory.GetLastWriteTimeUtc(stage) }
                        .Concat(files.SelectMany(file => new[] { File.GetCreationTimeUtc(file), File.GetLastWriteTimeUtc(file) }))
                        .Any(time => time > cutoff)) continue;
                    // Complete legacy stages and empty directories are recognizable. Partial stages need our activity marker.
                    var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
                    if (names.Count != 0 && !names.Contains(StagingWorkspace.ActivityFileName) &&
                        !names.SetEquals(["package.zip", "PrivacyMask.App.exe", "update-manifest.json", "update-manifest.sig.json"]))
                    { partial = true; continue; }
                    Quarantine(stage, "stage-" + id, staging: true);
                }
                catch (Exception error) when (IsPreservable(error)) { partial = true; }
            }
            return new(partial ? RetentionOutcome.Partial : RetentionOutcome.Completed, removed, bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (IsPreservable(error) || error is OperationCanceledException)
        { return new(RetentionOutcome.Partial, removed, bytes); }
        finally { foreach (var pin in pins) pin.Dispose(); }

        void Quarantine(string source, string name, bool staging)
        {
            AssertNoJournal();
            var root = Path.Combine(lease.Root, "update-pruning");
            if (!Path.Exists(root)) ManagedInstallLease.CreatePrivateDirectory(root);
            AssertDirectory(root);
            if (UpdateStoragePolicy.Catalog(lease, "update-pruning").Length >= UpdateStoragePolicy.MaximumQuarantined)
                throw new ReleaseValidationException("Cleanup quarantine capacity reached.");
            var destination = Path.Combine(root, name);
            if (Path.Exists(destination)) throw new ReleaseValidationException("Cleanup quarantine collision.");
            var files = Files(source, staging);
            AssertUnlocked(files);
            AssertDirectory(source);
            Directory.Move(source, destination); // Same-volume durable intent; never merge/overwrite another directory.
            checkpoint?.Invoke(RetentionCheckpoint.Quarantined);
            RemoveQuarantined(destination);
        }

        void RemoveQuarantined(string path)
        {
            token.ThrowIfCancellationRequested();
            AssertNoJournal();
            var parent = Path.Combine(lease.Root, "update-pruning");
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), parent, StringComparison.OrdinalIgnoreCase))
                throw new ReleaseValidationException("Cleanup path is outside quarantine.");
            var staging = UpdateStoragePolicy.TryId(path, "stage-", out var id);
            if (!staging && !UpdateStoragePolicy.TryId(path, "operation-", out id))
                throw new ReleaseValidationException("Unknown cleanup directory.");
            if (staging && id == protectedStageId) throw new ReleaseValidationException("Selected staging is protected.");
            var files = Files(path, staging);
            if (!staging && files.Length > 0) _ = ReadTerminal(path, id);
            AssertUnlocked(files);
            // Keep the terminal record until all other files have gone; an interrupted cleanup remains recognizable.
            foreach (var file in files.OrderBy(file => Path.GetFileName(file) == "terminal-journal.json"))
            {
                token.ThrowIfCancellationRequested();
                AssertNoJournal();
                ManagedInstallLease.AssertPrivate(file);
                var length = new FileInfo(file).Length;
                File.Delete(file);
                bytes = checked(bytes + length);
                checkpoint?.Invoke(RetentionCheckpoint.FileRemoved);
            }
            AssertDirectory(path);
            Directory.Delete(path, recursive: false);
            removed++;
            checkpoint?.Invoke(RetentionCheckpoint.DirectoryRemoved);
        }

        void AssertNoJournal()
        {
            lease.AssertHeld();
            if (new RecoveryJournalStore(lease).Read() is not null)
                throw new ReleaseValidationException("Pending recovery prevents cleanup.");
        }
    }

    private Terminal ReadTerminal(string directory, string id)
    {
        var path = Path.Combine(directory, "terminal-journal.json");
        ManagedInstallLease.AssertPrivate(path);
        var record = RecoveryJson.Read(ManagedInstallLease.ReadBounded(path, RecoveryRules.JournalLimit),
            RecoveryRules.JournalLimit, RecoveryJson.Context.RecoveryJournal);
        RecoveryRules.Validate(record);
        if (record.InstallId != lease.InstallId || record.OperationId != id ||
            record.Step is not (RecoveryStep.Committed or RecoveryStep.Recovered))
            throw new ReleaseValidationException("Cleanup requires matching terminal history.");
        return new(directory, id, record, File.GetLastWriteTimeUtc(path));
    }

    private static string[] Files(string directory, bool staging)
    {
        AssertDirectory(directory);
        var files = Directory.EnumerateFileSystemEntries(directory).Take(UpdateStoragePolicy.MaximumFiles + 1).ToArray();
        if (files.Length > UpdateStoragePolicy.MaximumFiles) throw new ReleaseValidationException("Cleanup file scan limit reached.");
        foreach (var file in files)
        {
            ManagedInstallLease.AssertPrivate(file);
            if ((File.GetAttributes(file) & FileAttributes.Directory) != 0 ||
                new FileInfo(file).Length > FileLimit(Path.GetFileName(file), staging))
                throw new ReleaseValidationException("Unknown or oversized cleanup evidence.");
        }
        return files;
    }

    private static long FileLimit(string name, bool staging)
    {
        if (name == "update-manifest.json") return ManifestRules.ManifestLimit;
        if (name == "update-manifest.sig.json") return ManifestRules.SignatureLimit;
        if (staging)
            return name switch {
                StagingWorkspace.ActivityFileName => 0,
                "package.zip" or "package.partial" or "payload.partial" or "PrivacyMask.App.exe" => ManifestRules.MaximumFileSize,
                _ => throw new ReleaseValidationException("Unknown staging file.") };
        if (name == "terminal-journal.json") return RecoveryRules.JournalLimit;
        if (name is "original-settings.json" or "original-settings.bak" or "failed-primary" or "failed-secondary") return RecoveryRules.SettingsLimit;
        if (name is "original-app.exe" or "target-app.exe" or "displaced-original-app.exe" or "forward-app.tmp" or "failed-app") return ManifestRules.MaximumFileSize;
        if (name == "restore-app.tmp") return ManifestRules.MaximumFileSize;
        if (name is "restore-primary.tmp" or "restore-secondary.tmp") return RecoveryRules.SettingsLimit;
        foreach (var label in new[] { "app", "primary", "secondary" })
        {
            var prefix = "restore-" + label + "-";
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal) &&
                RecoveryRules.IsId(name[prefix.Length..^4]))
                return label == "app" ? ManifestRules.MaximumFileSize : RecoveryRules.SettingsLimit;
        }
        throw new ReleaseValidationException("Unknown operation file.");
    }

    private static void AssertUnlocked(string[] files)
    {
        var handles = new List<FileStream>();
        try
        {
            foreach (var file in files)
                handles.Add(new(file, FileMode.Open, FileAccess.Read, FileShare.None, 1));
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
    }

    private static void AssertDirectory(string path)
    {
        ManagedInstallLease.AssertPrivate(path);
        if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
            throw new ReleaseValidationException("Cleanup expects a private directory.");
    }

    private static SemanticVersion ParseVersion(string value) =>
        SemanticVersion.TryParse(value, out var version) ? version : throw new ReleaseValidationException("Invalid terminal version.");

    private static bool IsPreservable(Exception error) => error is IOException or UnauthorizedAccessException or
        ReleaseValidationException or JsonException or SecurityException or DecoderFallbackException or
        CryptographicException or FormatException or OverflowException;
}
