using System.Linq;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class DefaultSettingsFactoryTests
{
    [Fact]
    public void MergeWithDefaults_PreservesPersistedZoneOverrides()
    {
        var factory = new DefaultSettingsFactory();
        var persisted = factory.Create();

        var whatsAppProfile = persisted.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp);
        whatsAppProfile.Zones[0].DisplayName = "Custom chats";
        whatsAppProfile.Zones[0].RelativeRect = new RelativeRect(0.05d, 0.08d, 0.33d, 0.82d);
        whatsAppProfile.Zones[0].Enabled = false;
        persisted.GlobalHotkeys[0].Enabled = false;

        var merged = factory.MergeWithDefaults(persisted);
        var mergedProfile = merged.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp);
        var mergedZone = mergedProfile.Zones.Single(zone => zone.ZoneId == "full-window");

        Assert.Equal("Custom chats", mergedZone.DisplayName);
        Assert.Equal(new RelativeRect(0.05d, 0.08d, 0.33d, 0.82d), mergedZone.RelativeRect);
        Assert.False(mergedZone.Enabled);
        Assert.False(merged.GlobalHotkeys.Single(binding => binding.Action == HotkeyAction.ToggleProtection).Enabled);
    }

    [Fact]
    public void MergeWithDefaults_MigratesLegacyFocusAwareProfiles_ToAlwaysVisibleDefault()
    {
        var factory = new DefaultSettingsFactory();
        var current = factory.Create();
        var persisted = new AppSettings
        {
            Version = 1,
            OnboardingCompleted = current.OnboardingCompleted,
            LaunchAtLogin = current.LaunchAtLogin,
            StartMinimized = current.StartMinimized,
            CurrentMode = current.CurrentMode,
            GlobalHotkeys = current.GlobalHotkeys,
            AppProfiles = current.AppProfiles,
        };

        persisted.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp).StartupMode = AppActivationMode.FocusAware;

        var merged = factory.MergeWithDefaults(persisted);

        Assert.Equal(AppActivationMode.Manual, merged.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp).StartupMode);
        Assert.Equal(AppSettings.CurrentVersion, merged.Version);
    }

    [Fact]
    public void MergeWithDefaults_MigratesLegacyMaskIntensity_ToStrongerDefault()
    {
        var factory = new DefaultSettingsFactory();
        var persisted = factory.Create();
        persisted = new AppSettings
        {
            Version = 2,
            OnboardingCompleted = persisted.OnboardingCompleted,
            LaunchAtLogin = persisted.LaunchAtLogin,
            StartMinimized = persisted.StartMinimized,
            CurrentMode = persisted.CurrentMode,
            GlobalHotkeys = persisted.GlobalHotkeys,
            AppProfiles = persisted.AppProfiles,
        };

        persisted.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).MaskIntensity = 1.0d;

        var merged = factory.MergeWithDefaults(persisted);

        Assert.Equal(1.35d, merged.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).MaskIntensity);
    }

    [Fact]
    public void MergeWithDefaults_MigratesLegacyHoverRevealSize_ToDefaults()
    {
        var factory = new DefaultSettingsFactory();
        var persisted = factory.Create();
        persisted = new AppSettings
        {
            Version = 3,
            OnboardingCompleted = persisted.OnboardingCompleted,
            LaunchAtLogin = persisted.LaunchAtLogin,
            StartMinimized = persisted.StartMinimized,
            CurrentMode = persisted.CurrentMode,
            GlobalHotkeys = persisted.GlobalHotkeys,
            AppProfiles = persisted.AppProfiles,
        };

        var whatsAppProfile = persisted.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp);
        whatsAppProfile.HoverRevealWidthPixels = 0;
        whatsAppProfile.HoverRevealHeightPixels = 0;

        var merged = factory.MergeWithDefaults(persisted);
        var mergedProfile = merged.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp);

        Assert.Equal(394, mergedProfile.HoverRevealWidthPixels);
        Assert.Equal(42, mergedProfile.HoverRevealHeightPixels);
    }
}
