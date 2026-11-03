using System.Diagnostics;
using System.IO;
using PrivacyMask.App.Services;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class InstalledLaunchRedirectTests
{
    [Fact]
    public void Copied_app_starts_validated_installed_launcher_without_forwarding_arbitrary_arguments()
    {
        using var fixture = new RecoveryFixture();
        var launcherPath = Path.Combine(fixture.Root, LauncherBoot.LauncherFileName);
        using (var launcher = ManagedInstallLease.CreatePrivateFile(launcherPath)) launcher.WriteByte(1);

        Assert.True(InstalledLaunchRedirect.TryLaunch(Environment.ProcessPath, fixture.Parent,
            ["--MINIMIZED", "--untrusted"], start: launch =>
            {
                Assert.Equal(launcherPath, launch.FileName);
                Assert.Equal(fixture.Root, launch.WorkingDirectory);
                Assert.False(launch.UseShellExecute);
                Assert.Equal(["--minimized"], launch.ArgumentList);
                return Process.GetCurrentProcess();
            }));
    }

    [Fact]
    public void Installed_app_does_not_relaunch_itself_and_no_install_keeps_portable_startup()
    {
        using var fixture = new RecoveryFixture();
        Assert.False(InstalledLaunchRedirect.TryLaunch(fixture.App, fixture.Parent, [], start: _ => throw new Exception()));
        Assert.False(InstalledLaunchRedirect.TryLaunch(Environment.ProcessPath, fixture.App + "-missing", [],
            start: _ => throw new Exception()));
    }

    [Fact]
    public void Broken_managed_install_does_not_launch_unverified_target_or_fall_back_to_portable()
    {
        using var fixture = new RecoveryFixture();
        Assert.Throws<FileNotFoundException>(() => InstalledLaunchRedirect.TryLaunch(
            Environment.ProcessPath, fixture.Parent, [], start: _ => throw new Exception()));
    }
}
