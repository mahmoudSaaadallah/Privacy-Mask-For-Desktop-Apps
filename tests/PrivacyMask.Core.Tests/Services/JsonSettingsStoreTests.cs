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

            Assert.Equal(AppSettings.CurrentVersion, recovered.Version);
            Assert.Contains(recovered.AppProfiles, profile => profile.AppId == AppId.WhatsApp);
            Assert.Contains($"\"version\": {AppSettings.CurrentVersion}", persistedText);
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
