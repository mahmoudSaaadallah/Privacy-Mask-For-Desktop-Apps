using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class CustomAppProfileFactoryTests
{
    [Fact]
    public void Create_BuildsSafeFullWindowProfile()
    {
        var profile = CustomAppProfileFactory.Create(
            "  Notes  ",
            "  Notepad  ",
            "custom-notes",
            "  C:\\Apps\\Notepad.exe  ");

        Assert.Equal("custom-notes", profile.ProfileId);
        Assert.Equal(AppId.Custom, profile.AppId);
        Assert.Equal("Notes", profile.DisplayName);
        Assert.Equal("C:\\Apps\\Notepad.exe", profile.ExecutablePath);
        Assert.Equal("Notepad", profile.WindowMatchers.Single().ProcessNames.Single());
        Assert.Equal(ProcessNameMatchMode.Exact, profile.WindowMatchers.Single().ProcessNameMatchMode);
        Assert.Equal("custom-full-window", profile.SelectedPresetId);
        Assert.Equal(new RelativeRect(0d, 0d, 1d, 1d), profile.Zones.Single().RelativeRect);
        Assert.Equal(MaskStyle.Blur, profile.Zones.Single().Style);
        Assert.Equal(
            ZoneBehavior.RevealOnHover | ZoneBehavior.HideDuringTemporaryReveal,
            profile.Zones.Single().Behavior);
        Assert.Equal(600, profile.HoverRevealWidthPixels);
        Assert.Equal(70, profile.HoverRevealHeightPixels);
    }

    [Fact]
    public void Create_GeneratesUniqueProfileIds()
    {
        var first = CustomAppProfileFactory.Create("First", "FirstApp");
        var second = CustomAppProfileFactory.Create("Second", "SecondApp");

        Assert.StartsWith("custom-", first.ProfileId);
        Assert.StartsWith("custom-", second.ProfileId);
        Assert.NotEqual(first.ProfileId, second.ProfileId);
    }
}
