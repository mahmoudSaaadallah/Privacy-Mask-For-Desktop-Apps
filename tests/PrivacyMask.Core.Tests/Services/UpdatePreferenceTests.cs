using System.Text.Json;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdatePreferenceTests
{
    [Theory]
    [InlineData("2.0.0", UpdateReleaseChannel.Stable)]
    [InlineData("2.0.0+source-beta", UpdateReleaseChannel.Stable)]
    [InlineData("2.0.0-beta.8+source", UpdateReleaseChannel.Preview)]
    [InlineData("2.0.0-rc.1", UpdateReleaseChannel.Preview)]
    public void InitialChannelMatchesProductPrerelease(string version, UpdateReleaseChannel expected) =>
        Assert.Equal(expected, UpdatePreferenceDefaults.ForVersion(version));

    [Fact]
    public void SchemaTwelveGetsDefaultsWithoutChangingProtection()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("""{"Version":12,"StartMinimized":false,"LaunchAtLogin":false,"OnboardingCompleted":true}""")!;
        var upgraded = new DefaultSettingsFactory().MergeWithDefaults(old);
        Assert.Equal(13, upgraded.Version);
        Assert.True(upgraded.AutomaticUpdateChecks);
        Assert.Equal(UpdatePreferenceDefaults.Channel, upgraded.UpdateChannel);
        Assert.False(upgraded.StartMinimized);
        Assert.False(upgraded.LaunchAtLogin);
        Assert.True(upgraded.OnboardingCompleted);
    }

    [Theory]
    [InlineData(UpdateReleaseChannel.Stable)]
    [InlineData(UpdateReleaseChannel.Preview)]
    public void SavedChoiceAndOptOutSurviveMergeAndClone(UpdateReleaseChannel channel)
    {
        var settings = new DefaultSettingsFactory().Create();
        settings.AutomaticUpdateChecks = false;
        settings.UpdateChannel = channel;
        var merged = new DefaultSettingsFactory().MergeWithDefaults(AppSettingsCloner.Clone(settings));
        Assert.False(merged.AutomaticUpdateChecks);
        Assert.Equal(channel, merged.UpdateChannel);
    }

    [Fact]
    public void InvalidChannelIsRejectedForSaveAndNormalizedOnLoad()
    {
        var settings = new DefaultSettingsFactory().Create();
        settings.UpdateChannel = (UpdateReleaseChannel)42;
        Assert.Contains(AppSettingsValidator.Validate(settings), error => error.ToString()!.Contains("Update channel"));
        Assert.Equal(UpdatePreferenceDefaults.Channel, new DefaultSettingsFactory().MergeWithDefaults(settings).UpdateChannel);
    }
}
