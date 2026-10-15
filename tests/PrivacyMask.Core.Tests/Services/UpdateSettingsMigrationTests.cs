using System.IO;
using System.Security.Cryptography;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateSettingsMigrationTests
{
    [Fact]
    public async Task Migration_preserves_PIN_profiles_and_startup_choices()
    {
        using var fixture = new SettingsFixture();
        var settings = fixture.Factory.Create();
        settings.OnboardingCompleted = true;
        settings.LaunchAtLogin = false;
        settings.StartMinimized = false;
        settings.PanicPin = PanicPinService.Create("2468");
        settings.AppProfiles.Add(CustomAppProfileFactory.Create("Notepad", "notepad", "custom-notepad"));
        await fixture.Store.SaveAsync(settings);
        fixture.Capture();

        var result = await fixture.Store.MigrateForUpdateAsync(fixture.Baseline());

        Assert.True(result.Settings.OnboardingCompleted);
        Assert.False(result.Settings.LaunchAtLogin);
        Assert.False(result.Settings.StartMinimized);
        Assert.True(PanicPinService.Verify("2468", result.Settings.PanicPin));
        Assert.Contains(result.Settings.AppProfiles, profile => profile.ProfileId == "custom-notepad");
        Assert.DoesNotContain("2468", await File.ReadAllTextAsync(fixture.Primary));
        Assert.Equal(AppSettings.CurrentVersion, result.Settings.Version);
    }

    [Fact]
    public async Task Migration_restores_an_exact_backup_when_primary_was_absent()
    {
        using var fixture = new SettingsFixture();
        var settings = fixture.Factory.Create();
        settings.OnboardingCompleted = true;
        await fixture.Store.SaveAsync(settings);
        File.Move(fixture.Primary, fixture.Backup);
        fixture.Capture();

        var result = await fixture.Store.MigrateForUpdateAsync(fixture.Baseline());

        Assert.True(result.Settings.OnboardingCompleted);
        Assert.True(File.Exists(fixture.Primary));
        Assert.True(File.Exists(fixture.Backup));
    }

    [Fact]
    public async Task Migration_creates_defaults_only_when_both_saved_files_were_absent()
    {
        using var fixture = new SettingsFixture();
        fixture.Capture();

        var result = await fixture.Store.MigrateForUpdateAsync(fixture.Baseline());

        Assert.True(result.Settings.LaunchAtLogin);
        Assert.True(result.Settings.StartMinimized);
        Assert.True(File.Exists(fixture.Primary));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{}")]
    [InlineData("{\"version\":999}")]
    public async Task Migration_fails_closed_without_replacing_malformed_settings(string content)
    {
        using var fixture = new SettingsFixture();
        await File.WriteAllTextAsync(fixture.Primary, content);
        fixture.Capture();
        var before = await File.ReadAllBytesAsync(fixture.Primary);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Store.MigrateForUpdateAsync(fixture.Baseline()));

        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Primary));
        Assert.False(File.Exists(fixture.Backup));
    }

    [Fact]
    public async Task Migration_rejects_a_live_file_changed_after_the_worker_snapshot()
    {
        using var fixture = new SettingsFixture();
        await fixture.Store.SaveAsync(fixture.Factory.Create());
        fixture.Capture();
        await File.AppendAllTextAsync(fixture.Primary, " ");

        await Assert.ThrowsAsync<SettingsUpdateException>(() =>
            fixture.Store.MigrateForUpdateAsync(fixture.Baseline()));
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("PrivacyMask-update-settings-").FullName;
        private SettingsFileSnapshot _primary = new(false, 0, null);
        private SettingsFileSnapshot _backup = new(false, 0, null);

        internal DefaultSettingsFactory Factory { get; } = new();
        internal string Primary => Path.Combine(_root, "settings.v1.json");
        internal string Backup => Primary + ".bak";
        internal string OriginalPrimary => Path.Combine(_root, "original-settings.json");
        internal string OriginalBackup => Path.Combine(_root, "original-settings.bak");
        internal JsonSettingsStore Store => new(Factory, Primary);

        internal void Capture()
        {
            _primary = Snapshot(Primary);
            _backup = Snapshot(Backup);
            if (_primary.Exists) File.Copy(Primary, OriginalPrimary);
            if (_backup.Exists) File.Copy(Backup, OriginalBackup);
        }

        internal UpdateSettingsBaseline Baseline() =>
            new(Primary, Backup, OriginalPrimary, OriginalBackup, _primary, _backup);

        private static SettingsFileSnapshot Snapshot(string path)
        {
            if (!File.Exists(path)) return new(false, 0, null);
            var bytes = File.ReadAllBytes(path);
            return new(true, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
