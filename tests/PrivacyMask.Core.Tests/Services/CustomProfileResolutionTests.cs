using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Adapters;

namespace PrivacyMask.Core.Tests.Services;

public sealed class CustomProfileResolutionTests
{
    [Fact]
    public void Resolve_SelectsTheMatchingProfileAmongMultipleCustomApps()
    {
        var notes = CustomAppProfileFactory.Create("Notes", "Notepad", "custom-notes");
        var browser = CustomAppProfileFactory.Create("Browser", "BrowserApp", "custom-browser");
        var resolver = new WindowProfileResolver([new GenericWindowAdapter()]);
        var snapshot = new WindowSnapshot
        {
            Handle = 42,
            ProcessName = "BrowserApp",
            Title = "Private browser",
            ClassName = "BrowserWindow",
            Bounds = new ScreenRect(0, 0, 1280, 720),
            IsVisible = true,
            IsForeground = true,
        };

        var tracked = resolver.Resolve(snapshot, [notes, browser]);

        Assert.NotNull(tracked);
        Assert.Equal("custom-browser", tracked.Profile.ProfileId);
        Assert.Equal("custom-full-window", tracked.Preset.PresetId);
        Assert.Equal(MaskIntensityScale.Default, tracked.EffectiveZones.Single().Strength);
    }
}
