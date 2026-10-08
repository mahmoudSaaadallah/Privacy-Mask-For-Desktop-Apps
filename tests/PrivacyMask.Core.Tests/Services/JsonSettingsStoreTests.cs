using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_CreatesDefaultsWhenFileDoesNotExist()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);

            var settings = await store.LoadAsync();

            Assert.True(File.Exists(settingsPath));
            Assert.Contains(settings.AppProfiles, profile => profile.AppId == AppId.WhatsApp);
            Assert.Contains(settings.AppProfiles, profile => profile.AppId == AppId.Telegram);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RoundTripsUpdatedSettings()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);
            var settings = await store.LoadAsync();
            settings.LaunchAtLogin = true;
            var telegramProfile = settings.AppProfiles.Single(profile => profile.AppId == AppId.Telegram);
            telegramProfile.Enabled = false;
            telegramProfile.MaskColor = MaskColorOption.Red;

            await store.SaveAsync(settings);
            var reloaded = await store.LoadAsync();

            Assert.True(reloaded.LaunchAtLogin);
            Assert.False(reloaded.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).Enabled);
            Assert.Equal(MaskColorOption.Red, reloaded.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).MaskColor);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RoundTripsCustomAppProfiles()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);
            var settings = factory.Create();
            settings.AppProfiles.Add(CustomAppProfileFactory.Create(
                "Windows Notepad",
                "Notepad",
                "custom-windows-notepad"));

            await store.SaveAsync(settings);
            var reloaded = await store.LoadAsync();

            var customProfile = Assert.Single(
                reloaded.AppProfiles,
                profile => profile.ProfileId == "custom-windows-notepad");
            Assert.Equal(AppId.Custom, customProfile.AppId);
            Assert.Equal("Windows Notepad", customProfile.DisplayName);
            var matcher = Assert.Single(customProfile.WindowMatchers);
            Assert.Equal("Notepad", Assert.Single(matcher.ProcessNames));
            Assert.Equal(ProcessNameMatchMode.Exact, matcher.ProcessNameMatchMode);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_SerializesConcurrentWritesWithoutLeavingTemporaryFiles()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);
            var first = factory.Create();
            first.LaunchAtLogin = true;
            var second = factory.Create();
            second.StartMinimized = true;

            await Task.WhenAll(store.SaveAsync(first), store.SaveAsync(second));

            var persisted = await store.LoadAsync();
            Assert.True(persisted.LaunchAtLogin || persisted.StartMinimized);
            Assert.Empty(Directory.EnumerateFiles(tempDirectory.FullName, "*.tmp"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_LeavesExistingFileUntouchedWhenCancelledBeforeWriting()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);
            var original = factory.Create();
            await store.SaveAsync(original);
            var originalText = await File.ReadAllTextAsync(settingsPath);
            var updated = factory.Create();
            updated.LaunchAtLogin = true;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(updated, cancellation.Token));

            Assert.Equal(originalText, await File.ReadAllTextAsync(settingsPath));
            Assert.Empty(Directory.EnumerateFiles(tempDirectory.FullName, "*.tmp"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RecoversDefaultsFromMalformedJson()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            await File.WriteAllTextAsync(settingsPath, "{ not-valid-json");
            var store = new JsonSettingsStore(factory, settingsPath);

            var recovered = await store.LoadAsync();
            var persistedText = await File.ReadAllTextAsync(settingsPath);
            var corruptPath = Assert.Single(Directory.EnumerateFiles(tempDirectory.FullName, "*.corrupt-*.json"));

            Assert.Equal(AppSettings.CurrentVersion, recovered.Version);
            Assert.Contains(recovered.AppProfiles, profile => profile.AppId == AppId.WhatsApp);
            Assert.Contains($"\"version\": {AppSettings.CurrentVersion}", persistedText);
            Assert.Equal("{ not-valid-json", await File.ReadAllTextAsync(corruptPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RecoversLastKnownGoodBackupAndPreservesMalformedPrimary()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);
            var lastKnownGood = factory.Create();
            lastKnownGood.LaunchAtLogin = true;
            lastKnownGood.StartMinimized = false;
            await store.SaveAsync(lastKnownGood);
            var newer = factory.Create();
            newer.StartMinimized = true;
            await store.SaveAsync(newer);
            await File.WriteAllTextAsync(settingsPath, "{ truncated");

            var recovered = await store.LoadAsync();

            Assert.True(recovered.LaunchAtLogin);
            Assert.False(recovered.StartMinimized);
            Assert.Equal("{ truncated", await File.ReadAllTextAsync(
                Assert.Single(Directory.EnumerateFiles(tempDirectory.FullName, "*.corrupt-*.json"))));
            Assert.True(File.Exists($"{settingsPath}.bak"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_DoesNotRewriteAlreadyNormalizedSettings()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);
            await store.SaveAsync(factory.Create());
            var expectedWriteTime = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(settingsPath, expectedWriteTime);

            await store.LoadAsync();

            Assert.Equal(expectedWriteTime, File.GetLastWriteTimeUtc(settingsPath));
            Assert.False(File.Exists($"{settingsPath}.bak"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_NormalizesPersistedRuntimeModeToProtected()
    {
        var factory = new DefaultSettingsFactory();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, settingsPath);
            await File.WriteAllTextAsync(settingsPath, "{\"version\":5,\"currentMode\":0}");

            var reloaded = await store.LoadAsync();
            var persistedText = await File.ReadAllTextAsync(settingsPath);

            Assert.Equal(RuntimeMode.Standard, reloaded.CurrentMode);
            Assert.Contains("\"currentMode\": 1", persistedText);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
