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
    public void MergeWithDefaults_MigratesLegacyMaskIntensity_ToSecureSurfaceScale()
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

        Assert.Equal(
            MaskIntensityScale.FromLegacy(1.35d),
            merged.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).MaskIntensity,
            3);
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

    [Fact]
    public void MergeWithDefaults_PreservesPersistedMaskColor()
    {
        var factory = new DefaultSettingsFactory();
        var persisted = factory.Create();
        persisted.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).MaskColor = MaskColorOption.Blue;

        var merged = factory.MergeWithDefaults(persisted);

        Assert.Equal(MaskColorOption.Blue, merged.AppProfiles.Single(profile => profile.AppId == AppId.Telegram).MaskColor);
    }

    [Fact]
    public void Create_ProvidesCompleteSupportedAppAndHotkeyDefaults()
    {
        var settings = new DefaultSettingsFactory().Create();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal([AppId.WhatsApp, AppId.Telegram], settings.AppProfiles.Select(profile => profile.AppId));
        Assert.Equal(
            [
                HotkeyAction.ToggleProtection,
                HotkeyAction.PanicHideAll,
                HotkeyAction.OpenSettings,
                HotkeyAction.TemporaryRevealHold,
                HotkeyAction.IncreaseBlurStrength,
            ],
            settings.GlobalHotkeys.Select(binding => binding.Action));
        Assert.All(settings.AppProfiles, profile => Assert.Single(profile.Zones));
        Assert.All(settings.AppProfiles, profile => Assert.Equal("full-window", profile.Zones.Single().ZoneId));
        Assert.Equal(
            "Panic mask all",
            settings.GlobalHotkeys.Single(binding => binding.Action == HotkeyAction.PanicHideAll).DisplayName);
        var increaseBlur = settings.GlobalHotkeys.Single(binding => binding.Action == HotkeyAction.IncreaseBlurStrength);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Windows, increaseBlur.Modifiers);
        Assert.Equal(0x26, increaseBlur.VirtualKey);
    }

    [Fact]
    public void MergeWithDefaults_AddsBlurShortcutToExistingSettings()
    {
        var factory = new DefaultSettingsFactory();
        var persisted = factory.Create();
        persisted.GlobalHotkeys.RemoveAll(binding => binding.Action == HotkeyAction.IncreaseBlurStrength);

        var merged = factory.MergeWithDefaults(persisted);

        var increaseBlur = merged.GlobalHotkeys.Single(binding => binding.Action == HotkeyAction.IncreaseBlurStrength);
        Assert.True(increaseBlur.Enabled);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Windows, increaseBlur.Modifiers);
        Assert.Equal(0x26, increaseBlur.VirtualKey);
    }

    [Fact]
    public void MergeWithDefaults_ClampsPersistedRuntimeValues()
    {
        var factory = new DefaultSettingsFactory();
        var persisted = factory.Create();
        var profile = persisted.AppProfiles.Single(candidate => candidate.AppId == AppId.Telegram);
        profile.MaskIntensity = 9.0d;
        profile.HoverRevealWidthPixels = 20;
        profile.HoverRevealHeightPixels = 900;

        var merged = factory.MergeWithDefaults(persisted);
        var mergedProfile = merged.AppProfiles.Single(candidate => candidate.AppId == AppId.Telegram);

        Assert.Equal(MaskIntensityScale.Maximum, mergedProfile.MaskIntensity);
        Assert.Equal(80, mergedProfile.HoverRevealWidthPixels);
        Assert.Equal(420, mergedProfile.HoverRevealHeightPixels);
    }

    [Fact]
    public void MergeWithDefaults_MigratesVersionSixProfileAndZoneIntensity()
    {
        var factory = new DefaultSettingsFactory();
        var current = factory.Create();
        var persisted = new AppSettings
        {
            Version = 6,
            OnboardingCompleted = current.OnboardingCompleted,
            LaunchAtLogin = current.LaunchAtLogin,
            StartMinimized = current.StartMinimized,
            CurrentMode = current.CurrentMode,
            GlobalHotkeys = current.GlobalHotkeys,
            AppProfiles = current.AppProfiles,
        };
        var profile = persisted.AppProfiles.Single(candidate => candidate.AppId == AppId.WhatsApp);
        profile.MaskIntensity = 1.35d;
        profile.Zones.Single().Strength = 2.40d;

        var merged = factory.MergeWithDefaults(persisted);
        var mergedProfile = merged.AppProfiles.Single(candidate => candidate.AppId == AppId.WhatsApp);

        Assert.Equal(MaskIntensityScale.FromLegacy(1.35d), mergedProfile.MaskIntensity, 3);
        Assert.Equal(MaskIntensityScale.Maximum, mergedProfile.Zones.Single().Strength);
        Assert.Equal(MaskStyle.Blur, mergedProfile.Zones.Single().Style);
    }

    [Theory]
    [InlineData(RuntimeMode.Off)]
    [InlineData(RuntimeMode.Panic)]
    [InlineData(RuntimeMode.TemporaryReveal)]
    public void MergeWithDefaults_DoesNotRestoreTransientRuntimeMode(RuntimeMode persistedMode)
    {
        var factory = new DefaultSettingsFactory();
        var persisted = factory.Create();
        persisted.CurrentMode = persistedMode;

        var merged = factory.MergeWithDefaults(persisted);

        Assert.Equal(RuntimeMode.Standard, merged.CurrentMode);
        Assert.Equal(AppSettings.CurrentVersion, merged.Version);
    }
}
