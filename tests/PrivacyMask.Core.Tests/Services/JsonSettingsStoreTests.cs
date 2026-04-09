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
            settings.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).Enabled = false;

            await store.SaveAsync(settings);
            var reloaded = await store.LoadAsync();

            Assert.True(reloaded.LaunchAtLogin);
            Assert.False(reloaded.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).Enabled);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
