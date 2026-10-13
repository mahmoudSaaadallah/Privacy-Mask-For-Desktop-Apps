using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class AboutAndSecurityPresentationTests
{
    [Fact]
    public void About_UsesVerifiedContactValuesAndAssemblyVersion()
    {
        Assert.Equal("Mahmoud Saadallah", AboutInformation.Developer);
        Assert.Equal("mahmoud.saadallah73@gmail.com", AboutInformation.SupportEmail);
        Assert.Equal("mailto:mahmoud.saadallah73@gmail.com", AboutInformation.EmailUri.AbsoluteUri);
        Assert.Equal("https://github.com/mahmoudsaaadallah", AboutInformation.GitHubUri.AbsoluteUri);
        Assert.Equal("https://linkedin.com/in/mahmoudsaaadallah", AboutInformation.LinkedInUri.AbsoluteUri);
        Assert.Equal($"Version {typeof(AboutInformation).Assembly.GetName().Version!.ToString(3)}", AboutInformation.VersionText);
    }

    [Fact]
    public void SecurityPinEdit_IsLocalPendingAndIndependentOfNavigation()
    {
        using var model = SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(),
            RuntimeMode.Standard, "test", new HashSet<HotkeyAction>());
        Assert.Equal("Add PIN", model.PanicPinActionLabel);
        model.SelectedPage = DesktopPage.Security;
        var credential = PanicPinService.Create("1234");
        model.SetPanicPin(credential);
        model.SelectedPage = DesktopPage.About;
        Assert.True(model.HasPendingChanges);
        Assert.Equal("Change PIN", model.PanicPinActionLabel);
        Assert.True(PanicPinService.Verify("1234", model.ToModel().PanicPin!));
        Assert.False(PanicPinService.Verify("9999", model.ToModel().PanicPin!));
    }
}
