using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ProductVersionFormatterTests
{
    [Theory]
    [InlineData("2.0.0-beta.1", "2.0.0-beta.1")]
    [InlineData("2.0.0-beta.2+abcdef1234", "2.0.0-beta.2")]
    [InlineData("2.0.0-rc.1+abcdef1234", "2.0.0-rc.1")]
    [InlineData("2.0.0+abcdef1234", "2.0.0")]
    [InlineData("2.1.0", "2.1.0")]
    [InlineData(null, "2.0.0")]
    [InlineData("", "2.0.0")]
    public void Display_RetainsPrereleaseButExcludesSourceMetadata(string? metadata, string expected)
    {
        Assert.Equal(expected, ProductVersionFormatter.Display(metadata, new Version(2, 0, 0, 0)));
    }

    [Fact]
    public void MissingMetadataAndAssembly_AreReportedAsUnknown()
    {
        Assert.Equal("unknown", ProductVersionFormatter.Display(null, null));
    }

    [Fact]
    public void BuildDescription_RetainsTheFullIdentityWithoutChangingTheVisibleVersion()
    {
        Assert.Contains(AboutInformation.FullVersion, AboutInformation.BuildDescription);
        Assert.Contains(AboutInformation.WindowsFileVersion, AboutInformation.BuildDescription);
        Assert.Equal("Version " + AboutInformation.ProductVersion, AboutInformation.VersionText);
        Assert.DoesNotContain('+', AboutInformation.ProductVersion);
    }
}
