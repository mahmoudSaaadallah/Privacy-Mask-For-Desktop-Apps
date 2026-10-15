using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ManagedStartupPathTests
{
    [Fact]
    public void Resolve_returns_the_private_stable_launcher_for_a_managed_app()
    {
        using var fixture = new RecoveryFixture();
        var launcherPath = Path.Combine(fixture.Root, LauncherBoot.LauncherFileName);
        using (var launcher = ManagedInstallLease.CreatePrivateFile(launcherPath)) launcher.WriteByte(1);

        Assert.Equal(launcherPath, ManagedStartupPath.Resolve(fixture.App));
    }

    [Fact]
    public void Resolve_leaves_portable_or_development_apps_unchanged()
    {
        Assert.Null(ManagedStartupPath.Resolve(Environment.ProcessPath!));
    }

    [Fact]
    public void Resolve_fails_closed_when_a_managed_launcher_is_missing()
    {
        using var fixture = new RecoveryFixture();

        Assert.Throws<FileNotFoundException>(() => ManagedStartupPath.Resolve(fixture.App));
    }
}
