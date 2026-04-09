using System.Collections.Generic;
using System.Linq;
using PrivacyMask.Core.Contracts;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class WindowProfileResolverTests
{
    [Fact]
    public void Resolve_UsesAdapterSelectedCompactPreset_WhenWindowIsNarrow()
    {
        var factory = new DefaultSettingsFactory();
        var settings = factory.Create();
        var resolver = new WindowProfileResolver([new FakeWhatsAppAdapter()]);
        var snapshot = new WindowSnapshot
        {
            Handle = 42,
            ProcessName = "WhatsApp",
            Title = "Chat window",
            ClassName = "ApplicationFrameWindow",
            Bounds = new ScreenRect(0, 0, 840, 900),
            IsVisible = true,
            IsForeground = true,
            IsMinimized = false,
        };

        var tracked = resolver.Resolve(snapshot, settings.AppProfiles);

        Assert.NotNull(tracked);
        Assert.Equal("compact", tracked!.Preset.LayoutVariant);
        Assert.Single(tracked.EffectiveZones);
        Assert.Equal("full-window", tracked.EffectiveZones.Single().ZoneId);
    }

    [Fact]
    public void Resolve_UsesProfileMaskIntensity_AsFinalZoneStrength()
    {
        var factory = new DefaultSettingsFactory();
        var settings = factory.Create();
        var whatsAppProfile = settings.AppProfiles.Single(profile => profile.AppId == AppId.WhatsApp);
        whatsAppProfile.MaskIntensity = 0.60d;

        var resolver = new WindowProfileResolver([new FakeWhatsAppAdapter()]);
        var snapshot = new WindowSnapshot
        {
            Handle = 88,
            ProcessName = "WhatsApp",
            Title = "Chat window",
            ClassName = "ApplicationFrameWindow",
            Bounds = new ScreenRect(0, 0, 1200, 900),
            IsVisible = true,
            IsForeground = true,
            IsMinimized = false,
        };

        var tracked = resolver.Resolve(snapshot, settings.AppProfiles);

        Assert.NotNull(tracked);
        var zone = tracked!.EffectiveZones.Single(item => item.ZoneId == "full-window");
        Assert.Equal(0.60d, zone.Strength, 2);
    }

    private sealed class FakeWhatsAppAdapter : IWindowAdapter
    {
        public AppId AppId => AppId.WhatsApp;

        public bool IsMatch(WindowSnapshot snapshot, AppProfile profile)
        {
            return profile.AppId == AppId.WhatsApp && snapshot.ProcessName == "WhatsApp";
        }

        public LayoutPreset SelectPreset(WindowSnapshot snapshot, AppProfile profile, IReadOnlyList<LayoutPreset> presets)
        {
            return snapshot.Bounds.Width >= 900
                ? presets.First(preset => preset.LayoutVariant == "wide")
                : presets.First(preset => preset.LayoutVariant == "compact");
        }
    }
}
