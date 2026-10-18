using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RetainedDuplicateTests
{
    [Fact]
    public async Task Committed_history_keeps_one_rollback_executable_instead_of_redundant_app_copies()
    {
        using var fixture = new RetentionFixture();
        var keep = fixture.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        RetentionFixture.Write(Path.Combine(keep, "target-app.exe"), fixture.Install.NewApp);
        RetentionFixture.Write(Path.Combine(keep, "forward-app.tmp"), fixture.Install.NewApp);
        var original = File.ReadAllBytes(Path.Combine(keep, "original-app.exe"));
        RetentionFixture.Write(Path.Combine(keep, "displaced-original-app.exe"), original);
        var result = await fixture.Run();
        Assert.Equal(RetentionOutcome.Completed, result.Outcome);
        Assert.Equal(0, result.RemovedDirectories);
        Assert.Equal(2 * fixture.Install.NewApp.Length + original.Length, result.RemovedBytes);
        Assert.False(File.Exists(Path.Combine(keep, "target-app.exe")));
        Assert.False(File.Exists(Path.Combine(keep, "displaced-original-app.exe")));
        Assert.False(File.Exists(Path.Combine(keep, "forward-app.tmp")));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(keep, "original-app.exe")));
        Assert.True(File.Exists(Path.Combine(keep, "original-settings.json")));
        Assert.True(File.Exists(Path.Combine(keep, "terminal-journal.json")));
        Assert.Equal(fixture.Install.NewApp, File.ReadAllBytes(fixture.Install.App));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_or_locked_optional_copies_are_preserved(bool locked)
    {
        using var fixture = new RetentionFixture();
        var keep = fixture.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var path = Path.Combine(keep, "target-app.exe");
        RetentionFixture.Write(path, locked ? fixture.Install.NewApp : [99]);
        using var handle = locked ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        Assert.Equal(RetentionOutcome.Partial, (await fixture.Run()).Outcome);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(keep, "original-app.exe")));
    }

    [Fact]
    public async Task Interrupted_optional_copy_deletion_leaves_the_retained_pair_valid_for_the_next_run()
    {
        using var fixture = new RetentionFixture();
        var keep = fixture.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        RetentionFixture.Write(Path.Combine(keep, "target-app.exe"), fixture.Install.NewApp);
        await Assert.ThrowsAsync<SimulatedRetentionCrash>(() => new UpdateRetention(fixture.Install.Lease, checkpoint: point => {
            if (point == RetentionCheckpoint.FileRemoved) throw new SimulatedRetentionCrash();
        }).RunAsync());
        Assert.True(File.Exists(Path.Combine(keep, "original-app.exe")));
        Assert.True(File.Exists(Path.Combine(keep, "terminal-journal.json")));
        Assert.Equal(RetentionOutcome.Completed, (await fixture.Run()).Outcome);
    }
}
