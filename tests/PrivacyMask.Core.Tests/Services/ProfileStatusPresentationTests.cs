using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ProfileStatusPresentationTests
{
    [Theory]
    [InlineData(ProfileRuntimeStatus.ProtectionPaused, "Protection paused")]
    [InlineData(ProfileRuntimeStatus.Disabled, "Disabled")]
    [InlineData(ProfileRuntimeStatus.NotRunning, "Not running")]
    [InlineData(ProfileRuntimeStatus.WaitingForFocus, "Waiting for focus")]
    [InlineData(ProfileRuntimeStatus.PanicProtected, "Panic mask active")]
    public void BadgesKeepProtectionSeparateFromProcessAndFocusState(ProfileRuntimeStatus runtime, string expected)
    {
        var model = new AppProfileViewModel(CustomAppProfileFactory.Create("Example", "example", "example"));
        model.UpdateRuntimeStatus(runtime);
        Assert.Equal(expected, model.StatusBadgeText);
        if (runtime == ProfileRuntimeStatus.ProtectionPaused)
        {
            Assert.Contains("windows may still be open", model.ApplicationHelp);
            Assert.Contains("Close or minimize", model.ApplicationHelp);
        }
    }
}
