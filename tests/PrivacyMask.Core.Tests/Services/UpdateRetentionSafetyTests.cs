using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateRetentionSafetyTests
{
    [Theory]
    [InlineData("update-staging")]
    [InlineData("update-pruning")]
    [InlineData("operation")]
    public async Task Junctions_never_allow_cleanup_to_follow_an_external_target(string location)
    {
        using var f = new RetentionFixture();
        var outside = Path.Combine(f.Install.Parent, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "keep external data");
        var link = location == "operation"
            ? Path.Combine(f.Install.Root, "update-operations", "operation-" + Guid.NewGuid().ToString("N"))
            : Path.Combine(f.Install.Root, location);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe") {
            Arguments = $"/d /c mklink /J \"{link}\" \"{outside}\"", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Equal(RetentionOutcome.Partial, (await f.Run()).Outcome);
            Assert.Equal("keep external data", File.ReadAllText(sentinel));
        }
        finally { Directory.Delete(link, recursive: false); }
    }

    [Fact]
    public async Task A_foreign_acl_or_file_inside_quarantine_is_not_silently_corrected_or_deleted()
    {
        using var f = new RetentionFixture();
        _ = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var older = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        var file = new FileInfo(Path.Combine(older, "original-settings.json"));
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(security);
        Assert.Equal(RetentionOutcome.Partial, (await f.Run()).Outcome);
        Assert.True(Directory.Exists(older));
        var quarantine = Path.Combine(f.Install.Root, "update-pruning");
        ManagedInstallLease.CreatePrivateDirectory(quarantine);
        var unknown = Path.Combine(quarantine, "stage-" + Guid.NewGuid().ToString("N"));
        ManagedInstallLease.CreatePrivateDirectory(unknown);
        RetentionFixture.Write(Path.Combine(unknown, "user-notes.txt"), [42]);
        Assert.Equal(RetentionOutcome.Partial, (await f.Run()).Outcome);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(Path.Combine(unknown, "user-notes.txt")));
    }

    [Fact]
    public async Task A_live_stager_is_protected_even_between_file_writes_and_with_a_future_cleanup_clock()
    {
        using var f = new RetentionFixture();
        var root = Path.Combine(f.Install.Root, "update-staging");
        ManagedInstallLease.CreatePrivateDirectory(root);
        var manifest = new UpdateManifest(1, ManifestRules.AppId, ManifestRules.Repository, "2.0.1", "v2.0.1",
            new string('1', 40), "stable", "win-x64", 19041, 1,
            new(ManifestRules.ArchiveName("2.0.1"), 1, new string('a', 64)),
            [new("PrivacyMask.App.exe", 1, new string('b', 64))], new(1, 12, 12));
        var workspace = new StagingWorkspace(root, manifest);
        try
        {
            var retention = new UpdateRetention(f.Install.Lease, new FutureClock());
            Assert.Equal(0, (await retention.RunAsync()).RemovedDirectories);
            Assert.True(File.Exists(Path.Combine(workspace.Root, StagingWorkspace.ActivityFileName)));
            workspace.Complete();
            Assert.Equal(1, (await retention.RunAsync()).RemovedDirectories);
        }
        finally { _ = workspace.Cleanup(); }
    }

    [Fact]
    public async Task A_broken_retained_backup_after_interruption_preserves_quarantined_history()
    {
        using var f = new RetentionFixture();
        var keep = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        _ = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        await Assert.ThrowsAsync<SimulatedRetentionCrash>(() => new UpdateRetention(f.Install.Lease, checkpoint: current => {
            if (current == RetentionCheckpoint.Quarantined) throw new SimulatedRetentionCrash();
        }).RunAsync());
        File.WriteAllBytes(Path.Combine(keep, "original-app.exe"), [99]);
        Assert.Equal(0, (await f.Run()).RemovedDirectories);
        Assert.Single(Directory.GetDirectories(Path.Combine(f.Install.Root, "update-pruning")));
    }

    [Fact]
    public async Task A_new_unknown_file_after_quarantine_is_preserved_before_any_delete()
    {
        using var f = new RetentionFixture();
        _ = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var old = f.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        var result = await new UpdateRetention(f.Install.Lease, checkpoint: point => {
            if (point == RetentionCheckpoint.Quarantined)
                RetentionFixture.Write(Path.Combine(f.Install.Root, "update-pruning", Path.GetFileName(old), "private.txt"), [7]);
        }).RunAsync();
        Assert.Equal(RetentionOutcome.Partial, result.Outcome);
        var moved = Path.Combine(f.Install.Root, "update-pruning", Path.GetFileName(old));
        Assert.True(File.Exists(Path.Combine(moved, "original-app.exe")));
        Assert.True(File.Exists(Path.Combine(moved, "private.txt")));
    }

    [Fact]
    public async Task The_latest_archive_breaks_ties_only_between_valid_matching_commits()
    {
        using var f = new RetentionFixture();
        var old = f.Archive("2.0.0-beta.2", "2.0.0-beta.4", installedTarget: true);
        var latest = f.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        File.SetLastWriteTimeUtc(Path.Combine(old, "terminal-journal.json"), DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(Path.Combine(latest, "terminal-journal.json"), DateTime.UtcNow.AddDays(-1));
        Assert.Equal(1, (await f.Run()).RemovedDirectories);
        Assert.True(Directory.Exists(latest));
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void Admission_counts_stage_and_quarantine_evidence_even_when_it_cannot_be_removed()
    {
        using var f = new RetentionFixture();
        for (var i = 0; i < UpdateStoragePolicy.MaximumStages; i++) f.Stage(stale: false);
        Assert.Throws<ReleaseValidationException>(() => UpdateStoragePolicy.RequireCapacity(f.Install.Lease, true));
        UpdateStoragePolicy.RequireCapacity(f.Install.Lease, false);
        var root = Path.Combine(f.Install.Root, "update-pruning");
        ManagedInstallLease.CreatePrivateDirectory(root);
        for (var i = 0; i < UpdateStoragePolicy.MaximumQuarantined; i++)
            RetentionFixture.Write(Path.Combine(root, "unknown-" + i), [1]);
        Assert.Throws<ReleaseValidationException>(() => UpdateStoragePolicy.RequireCapacity(f.Install.Lease, false));
    }

    private sealed class FutureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(2);
    }
}
