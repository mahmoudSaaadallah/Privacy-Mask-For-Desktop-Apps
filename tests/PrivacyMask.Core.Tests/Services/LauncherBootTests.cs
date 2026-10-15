using System.Diagnostics;
using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class LauncherBootTests
{
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
