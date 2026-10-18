using System.Diagnostics;
using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class LauncherBootTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Boot_runs_maintenance_and_cleanup_failure_does_not_block_the_verified_app(bool foreignFile)
    {
        using var fixture = new RetentionFixture();
        var keep = fixture.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var old = fixture.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        if (foreignFile) RetentionFixture.Write(Path.Combine(old, "private.txt"), [1]);
        var stage = fixture.Stage(stale: true);
        var launcher = Path.Combine(fixture.Install.Root, LauncherBoot.LauncherFileName);
        RetentionFixture.Write(launcher, [1]);
        fixture.Install.Lease.Dispose();
        var starts = 0;
        using var child = LauncherBoot.Launch(launcher, [], start => {
            starts++;
            Assert.Equal(fixture.Install.App, start.FileName);
            Assert.True(Directory.Exists(keep));
            Assert.Equal(foreignFile, Directory.Exists(old));
            Assert.False(Directory.Exists(stage));
            return Process.GetCurrentProcess();
        }, _ => { });
        Assert.Equal(1, starts);
    }

    [Fact]
    public void Boot_never_recovers_an_uncommitted_operation_while_the_managed_app_is_alive()
    {
        using var fixture = LauncherFixture();
        fixture.Lease.Dispose();
        Assert.Throws<ReleaseValidationException>(() => LauncherBoot.Launch(LauncherPath(fixture), [],
            _ => throw new Exception("Must not launch."), _ => { },
            path => { Assert.Equal(fixture.App, path); return true; }));
        fixture.Reopen();
        Assert.Equal(fixture.Record, new RecoveryJournalStore(fixture.Lease).Read());
        fixture.AssertOriginal();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Launch_recovers_pending_state_archives_it_and_starts_the_exact_app(bool minimized)
    {
        using var fixture = LauncherFixture();
        fixture.ReplaceFiles();
        fixture.Lease.Dispose();
        ProcessStartInfo? captured = null;

        using var child = LauncherBoot.Launch(LauncherPath(fixture), minimized ? ["--minimized"] : [],
            start => { captured = start; return Process.GetCurrentProcess(); }, _ => { });

        Assert.NotNull(captured);
        Assert.Equal(fixture.App, captured.FileName);
        Assert.Equal(fixture.Root, captured.WorkingDirectory);
        Assert.Equal(minimized ? ["--minimized"] : [], captured.ArgumentList);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "update-journal.json")));
        Assert.True(File.Exists(Path.Combine(fixture.Operation, "terminal-journal.json")));
        fixture.Reopen();
        fixture.AssertOriginal();
    }

    [Fact]
    public void Launch_archives_a_durable_commit_without_rolling_back_the_target()
    {
        using var fixture = LauncherFixture();
        fixture.Advance(RecoveryStep.AwaitingReadiness);
        File.WriteAllBytes(fixture.App, fixture.NewApp);
        fixture.Advance(RecoveryStep.Committed);
        fixture.Lease.Dispose();

        using var child = LauncherBoot.Launch(LauncherPath(fixture), [], _ => Process.GetCurrentProcess(), _ => { });

        Assert.Equal(fixture.NewApp, File.ReadAllBytes(fixture.App));
        Assert.True(File.Exists(Path.Combine(fixture.Operation, "terminal-journal.json")));
    }

    [Fact]
    public void Launch_refuses_repair_state_without_starting_an_app()
    {
        using var fixture = LauncherFixture();
        fixture.ReplaceFiles();
        File.Delete(Path.Combine(fixture.Operation, "original-app.exe"));
        fixture.Lease.Dispose();
        var starts = 0;

        Assert.Throws<ReleaseValidationException>(() => LauncherBoot.Launch(LauncherPath(fixture), [],
            _ => { starts++; return Process.GetCurrentProcess(); }, _ => { }));

        Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData("--update-start")]
    [InlineData("--MINIMIZED")]
    [InlineData("extra")]
    public void Launch_rejects_non_user_startup_arguments(string argument)
    {
        using var fixture = LauncherFixture();
        fixture.Lease.Dispose();

        Assert.Throws<ReleaseValidationException>(() => LauncherBoot.Launch(
            LauncherPath(fixture), [argument], _ => Process.GetCurrentProcess(), _ => { }));
    }

    private static RecoveryFixture LauncherFixture()
    {
        var fixture = new RecoveryFixture();
        using var launcher = ManagedInstallLease.CreatePrivateFile(LauncherPath(fixture));
        launcher.WriteByte(1);
        return fixture;
    }

    private static string LauncherPath(RecoveryFixture fixture) =>
        Path.Combine(fixture.Root, LauncherBoot.LauncherFileName);
}
