using System.Text.Json;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class AboutReferenceTests
{
    [Fact]
    public void DisplayedProfiles_AreDerivedFromTheActualNavigationUris()
    {
        Assert.Equal(AboutInformation.GitHubUri.Host + AboutInformation.GitHubUri.AbsolutePath.TrimEnd('/'), AboutInformation.GitHubDisplay);
        Assert.Equal(AboutInformation.LinkedInUri.Host + AboutInformation.LinkedInUri.AbsolutePath.TrimEnd('/'), AboutInformation.LinkedInDisplay);
        Assert.DoesNotContain("https://", AboutInformation.GitHubDisplay);
        Assert.Equal("mailto:" + AboutInformation.SupportEmail, AboutInformation.EmailUri.AbsoluteUri);
    }

    [Fact]
    public void AboutNavigation_DoesNotPersistOrDiscardPendingProfileEdits()
    {
        using var session = SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(), RuntimeMode.Standard, "synthetic", new HashSet<HotkeyAction>());
        session.AppProfiles[0].MaskIntensityPercent = 75;
        var before = JsonSerializer.Serialize(session.ToModel());
        session.SelectedPage = DesktopPage.About;
        Assert.True(session.HasPendingChanges);
        Assert.Equal(before, JsonSerializer.Serialize(session.ToModel()));
        session.SelectedPage = DesktopPage.Overview;
        Assert.Equal(75, session.AppProfiles[0].MaskIntensityPercent);
    }
}
