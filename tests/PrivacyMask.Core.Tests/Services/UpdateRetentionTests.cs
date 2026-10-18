using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateRetentionTests
{
    [Fact]
    public async Task Keeps_the_matching_committed_predecessor_and_never_changes_live_settings()
    {
        using var f = new RetentionFixture();
        var older = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        var previous = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var recovered = f.Archive("2.0.0-beta.4", "2.0.0-beta.5", recovered: true);
        var primary = File.ReadAllBytes(f.Install.Primary);
        var backup = File.ReadAllBytes(f.Install.Secondary);
        var result = await f.Run();
        Assert.Equal(RetentionOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.RemovedDirectories);
        Assert.True(result.RemovedBytes > 0);
        Assert.False(Directory.Exists(older));
        Assert.False(Directory.Exists(recovered));
        Assert.True(File.Exists(Path.Combine(previous, "original-app.exe")));
        Assert.Equal(primary, File.ReadAllBytes(f.Install.Primary));
        Assert.Equal(backup, File.ReadAllBytes(f.Install.Secondary));
        Assert.Equal(0, (await f.Run()).RemovedDirectories);
    }

    [Theory]
    [InlineData((int)RecoveryStep.Prepared)]
    [InlineData((int)RecoveryStep.Handoff)]
    [InlineData((int)RecoveryStep.Replacing)]
    [InlineData((int)RecoveryStep.AwaitingReadiness)]
    [InlineData((int)RecoveryStep.Committed)]
    [InlineData((int)RecoveryStep.RollingBack)]
    [InlineData((int)RecoveryStep.Recovered)]
    public async Task Every_active_journal_defers_all_cleanup(int step)
    {
        using var f = new RetentionFixture();
        var stage = f.Stage(stale: true);
        ManagedInstallLease.CreatePrivateDirectory(f.Install.Operation);
        RetentionFixture.Write(Path.Combine(f.Install.Operation, "original-app.exe"), f.Install.OldApp);
        var record = f.Install.Record with { Step = (RecoveryStep)step };
        RetentionFixture.Write(Path.Combine(f.Install.Root, "update-journal.json"), ContractJson.Write(record));
        Assert.Equal(RetentionOutcome.Deferred, (await f.Run()).Outcome);
        Assert.True(Directory.Exists(stage));
        Assert.True(Directory.Exists(f.Install.Operation));
    }

    [Theory]
    [InlineData("malformed-journal")]
    [InlineData("unknown-file")]
    [InlineData("foreign-install")]
    [InlineData("wrong-id")]
    [InlineData("nonterminal")]
    [InlineData("nested-directory")]
    public async Task Ambiguous_history_is_preserved(string change)
    {
        using var f = new RetentionFixture();
        _ = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var candidate = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        var archive = Path.Combine(candidate, "terminal-journal.json");
        var record = ContractJson.Read<RecoveryJournal>(File.ReadAllBytes(archive), RecoveryRules.JournalLimit);
        if (change == "malformed-journal") File.WriteAllText(archive, "broken JSON");
        if (change == "unknown-file") RetentionFixture.Write(Path.Combine(candidate, "notes.txt"), [1]);
        if (change == "nested-directory") ManagedInstallLease.CreatePrivateDirectory(Path.Combine(candidate, "nested"));
        if (change is "foreign-install" or "wrong-id" or "nonterminal")
            File.WriteAllBytes(archive, ContractJson.Write(change switch {
                "foreign-install" => record with { InstallId = Guid.NewGuid().ToString("N") },
                "wrong-id" => record with { OperationId = Guid.NewGuid().ToString("N") },
                _ => record with { Step = RecoveryStep.Prepared } }));
        Assert.Equal(RetentionOutcome.Partial, (await f.Run()).Outcome);
        Assert.True(File.Exists(Path.Combine(candidate, "original-app.exe")));
        Assert.True(File.Exists(archive));
    }

    [Theory]
    [InlineData("original-app.exe")]
    [InlineData("original-settings.json")]
    [InlineData("original-settings.bak")]
    [InlineData("current-app")]
    public async Task A_missing_or_changed_retained_baseline_prevents_terminal_pruning(string name)
    {
        using var f = new RetentionFixture();
        var keep = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var older = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        File.WriteAllBytes(name == "current-app" ? f.Install.App : Path.Combine(keep, name), [99]);
        Assert.Equal(RetentionOutcome.Partial, (await f.Run()).Outcome);
        Assert.True(Directory.Exists(older));
        Assert.True(Directory.Exists(keep));
    }

    [Fact]
    public async Task Orphaned_operation_backups_never_expire_and_count_toward_admission_capacity()
    {
        using var f = new RetentionFixture();
        _ = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        for (var i = 1; i < UpdateStoragePolicy.MaximumOperations; i++)
        {
            var orphan = Path.Combine(f.Install.Root, "update-operations", "operation-" + Guid.NewGuid().ToString("N"));
            ManagedInstallLease.CreatePrivateDirectory(orphan);
            RetentionFixture.Write(Path.Combine(orphan, "original-app.exe"), [1]);
            Directory.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddYears(-1));
        }
        Assert.Equal(0, (await f.Run()).RemovedDirectories);
        Assert.Throws<ReleaseValidationException>(() => UpdateStoragePolicy.RequireCapacity(f.Install.Lease, false));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("partial")]
    [InlineData("empty")]
    public async Task Expired_known_staging_is_removed_without_requiring_update_trust(string kind)
    {
        using var f = new RetentionFixture();
        var path = f.Stage(stale: true, kind);
        Assert.Equal(1, (await f.Run()).RemovedDirectories);
        Assert.False(Directory.Exists(path));
        Assert.Equal(f.Install.OldApp, File.ReadAllBytes(f.Install.App));
    }

    [Theory]
    [InlineData("fresh")]
    [InlineData("protected")]
    [InlineData("future")]
    [InlineData("legacy-partial")]
    [InlineData("foreign-file")]
    [InlineData("locked")]
    public async Task Current_unknown_or_busy_staging_is_not_touched(string reason)
    {
        using var f = new RetentionFixture();
        var stage = f.Stage(stale: reason != "fresh", reason is "locked" or "legacy-partial" ? "partial" : "complete");
        var marker = Path.Combine(stage, StagingWorkspace.ActivityFileName);
        if (reason == "legacy-partial") File.Delete(marker);
        if (reason == "foreign-file") RetentionFixture.Write(Path.Combine(stage, "private-notes.txt"), [1]);
        if (reason == "future") Directory.SetLastWriteTimeUtc(stage, DateTime.UtcNow.AddDays(1));
        using var locked = reason == "locked" ? new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        var id = Path.GetFileName(stage)["operation-".Length..];
        Assert.Equal(0, (await f.Run(reason == "protected" ? id : null)).RemovedDirectories);
        Assert.True(Directory.Exists(stage));
    }

    [Theory]
    [InlineData((int)RetentionCheckpoint.Quarantined)]
    [InlineData((int)RetentionCheckpoint.FileRemoved)]
    public async Task Interrupted_cleanup_resumes_only_inside_quarantine(int point)
    {
        using var f = new RetentionFixture();
        var keep = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var old = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        await Assert.ThrowsAsync<SimulatedRetentionCrash>(() => new UpdateRetention(f.Install.Lease, checkpoint: current => {
            if (current == (RetentionCheckpoint)point) throw new SimulatedRetentionCrash();
        }).RunAsync());
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(keep));
        Assert.Single(Directory.GetDirectories(Path.Combine(f.Install.Root, "update-pruning")));
        Assert.Equal(1, (await f.Run()).RemovedDirectories);
        Assert.Empty(Directory.GetDirectories(Path.Combine(f.Install.Root, "update-pruning")));
        Assert.True(File.Exists(Path.Combine(keep, "original-app.exe")));
    }

    [Fact]
    public async Task Locks_preserve_the_entire_source_before_quarantine_and_can_be_retried()
    {
        using var f = new RetentionFixture();
        _ = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var old = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        using (var locked = new FileStream(Path.Combine(old, "original-app.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(0, (await f.Run()).RemovedDirectories);
            Assert.True(File.Exists(Path.Combine(old, "terminal-journal.json")));
        }
        Assert.Equal(1, (await f.Run()).RemovedDirectories);
    }

    [Fact]
    public async Task A_scan_or_work_limit_bounds_allocations_and_preserves_excess_evidence()
    {
        using var f = new RetentionFixture();
        for (var i = 0; i < UpdateStoragePolicy.MaximumRemovalsPerRun + 2; i++) f.Stage(stale: true);
        Assert.Equal(UpdateStoragePolicy.MaximumRemovalsPerRun, (await f.Run()).RemovedDirectories);
        Assert.Equal(2, (await f.Run()).RemovedDirectories);
        for (var i = 0; i <= UpdateStoragePolicy.MaximumCatalogEntries; i++) f.Stage(stale: true, "empty");
        Assert.Equal(RetentionOutcome.Partial, (await f.Run()).Outcome);
        Assert.Equal(UpdateStoragePolicy.MaximumCatalogEntries + 1, Directory.GetDirectories(Path.Combine(f.Install.Root, "update-staging")).Length);
        Assert.Throws<ReleaseValidationException>(() => UpdateStoragePolicy.RequireCapacity(f.Install.Lease, true));
    }

    [Fact]
    public async Task Malformed_active_journal_and_cancellation_never_authorize_cleanup()
    {
        using var f = new RetentionFixture();
        var stage = f.Stage(stale: true);
        File.WriteAllText(Path.Combine(f.Install.Root, "update-journal.json"), "broken");
        Assert.Equal(RetentionOutcome.Partial, (await f.Run()).Outcome);
        Assert.True(Directory.Exists(stage));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateRetention(f.Install.Lease).RunAsync(
            cancellationToken: new CancellationToken(true)));
        Assert.True(Directory.Exists(stage));
    }
}

internal sealed class SimulatedRetentionCrash : Exception;

internal sealed class RetentionFixture : IDisposable
{
    internal RecoveryFixture Install { get; }
    internal RetentionFixture(bool primary = true, bool backup = true, bool empty = false)
    {
        Install = new(primary, backup, empty);
        File.Delete(Path.Combine(Install.Root, "update-journal.json"));
        foreach (var file in Directory.GetFiles(Install.Operation)) File.Delete(file);
        Directory.Delete(Install.Operation, recursive: false);
    }

    internal Task<RetentionResult> Run(string? protectedId = null) => new UpdateRetention(Install.Lease).RunAsync(protectedId);

    internal string Archive(string originalVersion, string targetVersion, bool installedTarget = false, bool recovered = false)
    {
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Install.Root, "update-operations", "operation-" + id);
        ManagedInstallLease.CreatePrivateDirectory(path);
        var original = System.Text.Encoding.UTF8.GetBytes("original " + originalVersion);
        var target = installedTarget ? Install.NewApp : System.Text.Encoding.UTF8.GetBytes("target " + targetVersion);
        Write(Path.Combine(path, "original-app.exe"), original);
        if (Install.OldPrimary is not null) Write(Path.Combine(path, "original-settings.json"), Install.OldPrimary);
        if (Install.OldSecondary is not null) Write(Path.Combine(path, "original-settings.bak"), Install.OldSecondary);
        var record = Install.Record with { OperationId = id, OriginalVersion = originalVersion, TargetVersion = targetVersion,
            OriginalApp = RecoveryFixture.Snapshot(original), TargetApp = RecoveryFixture.Snapshot(target),
            Step = recovered ? RecoveryStep.Recovered : RecoveryStep.Committed, Revision = 5 };
        Write(Path.Combine(path, "terminal-journal.json"), ContractJson.Write(record));
        if (installedTarget) File.WriteAllBytes(Install.App, target);
        return path;
    }

    internal string Stage(bool stale, string kind = "complete")
    {
        var root = Path.Combine(Install.Root, "update-staging");
        if (!Directory.Exists(root)) ManagedInstallLease.CreatePrivateDirectory(root);
        var path = Path.Combine(root, "operation-" + Guid.NewGuid().ToString("N"));
        ManagedInstallLease.CreatePrivateDirectory(path);
        var names = kind switch {
            "empty" => Array.Empty<string>(),
            "partial" => new[] { StagingWorkspace.ActivityFileName, "package.partial" },
            _ => new[] { "package.zip", "PrivacyMask.App.exe", "update-manifest.json", "update-manifest.sig.json" } };
        foreach (var name in names) Write(Path.Combine(path, name), name == StagingWorkspace.ActivityFileName ? [] : [1]);
        if (stale)
        {
            var then = DateTime.UtcNow.AddDays(-2);
            foreach (var file in Directory.GetFiles(path)) { File.SetCreationTimeUtc(file, then); File.SetLastWriteTimeUtc(file, then); }
            Directory.SetCreationTimeUtc(path, then);
            Directory.SetLastWriteTimeUtc(path, then);
        }
        return path;
    }

    internal static void Write(string path, byte[] bytes) { using var file = ManagedInstallLease.CreatePrivateFile(path); file.Write(bytes); }
    public void Dispose() => Install.Dispose();
}
