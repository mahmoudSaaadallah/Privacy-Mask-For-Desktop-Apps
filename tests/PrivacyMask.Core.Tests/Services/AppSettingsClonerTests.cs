using System.Linq;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class AppSettingsClonerTests
{
    [Fact]
    public void Clone_CopiesTheCompleteSettingsGraph()
    {
        var source = new DefaultSettingsFactory().Create();
        var sourceProfile = source.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp);
        source.OnboardingCompleted = true;
        source.LaunchAtLogin = true;
        source.CurrentMode = RuntimeMode.Panic;
        sourceProfile.Hotkeys.Add(new HotkeyBinding
        {
            Action = HotkeyAction.OpenSettings,
            DisplayName = "Profile shortcut",
            VirtualKey = 0x4F,
        });

        var clone = AppSettingsCloner.Clone(source);
        var clonedProfile = clone.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp);

        Assert.Equal(source.Version, clone.Version);
        Assert.Equal(source.OnboardingCompleted, clone.OnboardingCompleted);
        Assert.Equal(source.LaunchAtLogin, clone.LaunchAtLogin);
        Assert.Equal(source.CurrentMode, clone.CurrentMode);
        Assert.Equal(source.GlobalHotkeys.Count, clone.GlobalHotkeys.Count);
        Assert.Equal(sourceProfile.WindowMatchers.Single().ProcessNames, clonedProfile.WindowMatchers.Single().ProcessNames);
        Assert.Equal(sourceProfile.Zones.Single().RelativeRect, clonedProfile.Zones.Single().RelativeRect);
        Assert.Equal(sourceProfile.Presets.Count, clonedProfile.Presets.Count);
        Assert.Equal(sourceProfile.Hotkeys.Single().DisplayName, clonedProfile.Hotkeys.Single().DisplayName);
    }

    [Fact]
    public void Clone_DoesNotShareMutableNestedObjects()
    {
        var source = new DefaultSettingsFactory().Create();
        var clone = AppSettingsCloner.Clone(source);
        var sourceProfile = source.AppProfiles.Single(profile => profile.AppId == AppId.Telegram);
        var clonedProfile = clone.AppProfiles.Single(profile => profile.AppId == AppId.Telegram);

        clone.GlobalHotkeys[0].Enabled = false;
        clonedProfile.Enabled = false;
        clonedProfile.WindowMatchers[0].ProcessNames[0] = "ChangedProcess";
        clonedProfile.Zones[0].DisplayName = "Changed zone";
        clonedProfile.Presets[0].Zones[0].Enabled = false;

        Assert.True(source.GlobalHotkeys[0].Enabled);
        Assert.True(sourceProfile.Enabled);
        Assert.NotEqual("ChangedProcess", sourceProfile.WindowMatchers[0].ProcessNames[0]);
        Assert.NotEqual("Changed zone", sourceProfile.Zones[0].DisplayName);
        Assert.True(sourceProfile.Presets[0].Zones[0].Enabled);
    }
}
