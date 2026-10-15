using System.IO;
using System.Security.Cryptography;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateStartLayoutTests
{
    [Fact]
    public void Open_accepts_only_the_exact_awaiting_readiness_installation()
    {
        using var fixture = AwaitingFixture();
        var request = Request(fixture);

        var layout = UpdateStartLayout.Open(request, fixture.App, fixture.Root);

        Assert.Equal(fixture.Root, layout.Root);
        Assert.Equal(fixture.App, layout.ApplicationPath);
        Assert.Equal(fixture.Record, layout.Journal);
        Assert.Equal(fixture.Record.SettingsPrimary, layout.Settings.Primary);
        Assert.Equal(fixture.Record.SettingsBackup, layout.Settings.Backup);
    }

    [Fact]
    public void Open_rejects_a_request_before_the_readiness_checkpoint()
    {
        using var fixture = PreparedFixture();

        Assert.Throws<ReleaseValidationException>(() =>
            UpdateStartLayout.Open(Request(fixture), fixture.App, fixture.Root));
    }

    [Theory]
    [InlineData("install")]
    [InlineData("operation")]
    [InlineData("application")]
    [InlineData("settings")]
    [InlineData("launcher")]
    public void Open_rejects_changed_identity_or_private_state(string change)
    {
        using var fixture = AwaitingFixture();
        var request = Request(fixture);
        var executable = fixture.App;
        if (change == "install") request = request with { InstallId = Guid.NewGuid().ToString("N") };
        if (change == "operation") request = request with { OperationId = Guid.NewGuid().ToString("N") };
        if (change == "application") File.WriteAllText(fixture.App, "changed target");
        if (change == "settings") File.WriteAllText(fixture.Primary, "changed settings");
        if (change == "launcher") File.Delete(Path.Combine(fixture.Root, "PrivacyMask.Launcher.exe"));

        Assert.ThrowsAny<Exception>(() => UpdateStartLayout.Open(request, executable, fixture.Root));
    }

    [Fact]
    public void ReadCurrentJournal_accepts_only_the_same_operation()
    {
        using var fixture = AwaitingFixture();
        var layout = UpdateStartLayout.Open(Request(fixture), fixture.App, fixture.Root);

        Assert.Equal(RecoveryStep.AwaitingReadiness, layout.ReadCurrentJournal().Step);
        layout.AssertApplicationUnchanged();
    }

    [Fact]
    public void AssertMigratedSettings_pins_the_post_migration_files()
    {
        using var fixture = AwaitingFixture();
        var layout = UpdateStartLayout.Open(Request(fixture), fixture.App, fixture.Root);
        var primary = File.ReadAllBytes(fixture.Primary);
        var backup = File.ReadAllBytes(fixture.Secondary);
        var expected = new UpdateSettingsState(
            new(true, primary.Length, Convert.ToHexStringLower(SHA256.HashData(primary))),
            new(true, backup.Length, Convert.ToHexStringLower(SHA256.HashData(backup))));

        layout.AssertMigratedSettings(expected);
        File.AppendAllText(fixture.Primary, "changed");

        Assert.Throws<ReleaseValidationException>(() => layout.AssertMigratedSettings(expected));
    }

    private static RecoveryFixture PreparedFixture()
    {
        var fixture = new RecoveryFixture();
        AddLauncher(fixture);
        return fixture;
    }

    private static RecoveryFixture AwaitingFixture()
    {
        var fixture = PreparedFixture();
        fixture.Advance(RecoveryStep.AwaitingReadiness);
        File.WriteAllBytes(fixture.App, fixture.NewApp);
        File.WriteAllBytes(Path.Combine(fixture.Operation, "displaced-original-app.exe"), fixture.OldApp);
        return fixture;
    }

    private static void AddLauncher(RecoveryFixture fixture)
    {
        using var launcher = ManagedInstallLease.CreatePrivateFile(Path.Combine(fixture.Root, "PrivacyMask.Launcher.exe"));
        launcher.WriteByte(1);
    }

    private static UpdateStartRequest Request(RecoveryFixture fixture) => new(
        UpdateStartRequest.PipePrefix + Guid.NewGuid().ToString("N"), fixture.InstallId, fixture.OperationId,
        Environment.ProcessId, DateTime.UtcNow.Ticks, UpdateStartMode.Minimized);
}
