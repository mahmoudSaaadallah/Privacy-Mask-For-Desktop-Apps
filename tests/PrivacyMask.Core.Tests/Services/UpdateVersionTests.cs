using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("2.0.0", "2.0.1")]
    [InlineData("2.0.99", "2.1.0")]
    [InlineData("2.0.0-Z", "2.0.0-a")]
    [InlineData("2.0.0-999999999999999999999999", "2.0.0-1000000000000000000000000")]
    [InlineData("999999999999999999999999.0.0", "1000000000000000000000000.0.0")]
    public void FollowsSemVerPrecedenceWithoutOverflow(string lower, string higher)
    {
        Assert.True(SemanticVersion.TryParse(lower, out var a));
        Assert.True(SemanticVersion.TryParse(higher, out var b));
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("02.0.0")]
    [InlineData("2.0.0-beta.01")]
    [InlineData("2.0.0+")]
    [InlineData("2.0.0-.")]
    [InlineData("2.0.0\n")]
    [InlineData(" 2.0.0")]
    public void RejectsMalformedVersions(string value) => Assert.False(SemanticVersion.TryParse(value, out _));

    [Fact]
    public void BuildMetadataDoesNotAffectPrecedenceOrEquality()
    {
        Assert.True(SemanticVersion.TryParse("2.0.0-beta.2+abc.01", out var a));
        Assert.True(SemanticVersion.TryParse("2.0.0-beta.2+different", out var b));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("2.0.0-beta.2", a.ProductVersion);
    }

    [Theory]
    [InlineData(UpdateChannel.Stable, "2.0.0-beta.10", true, false)]
    [InlineData(UpdateChannel.Preview, "2.0.0-beta.10", true, true)]
    [InlineData(UpdateChannel.Preview, "2.0.0", false, true)]
    [InlineData(UpdateChannel.Stable, "2.0.0", false, true)]
    [InlineData(UpdateChannel.Preview, "2.0.0-beta.2", true, false)]
    [InlineData(UpdateChannel.Stable, "1.9.9", false, false)]
    [InlineData(UpdateChannel.Preview, "2.0.0-beta.3", false, false)]
    [InlineData(UpdateChannel.Preview, "2.0.0+new-hash", false, false)]
    public void ChannelAndInstalledMetadataNeverAuthorizeDowngrade(UpdateChannel channel, string candidate, bool pre, bool allowed)
    {
        var context = Context with { Channel = channel };
        Assert.Equal(allowed, UpdateEligibility.IsNewerForChannel(candidate, pre, context));
    }

    [Theory]
    [InlineData(InstallationKind.Unknown)]
    [InlineData(InstallationKind.Source)]
    [InlineData(InstallationKind.Portable)]
    [InlineData(InstallationKind.LegacyInstalled)]
    public void UnsupportedInstallationsRequireManualBootstrap(InstallationKind kind) =>
        Assert.Equal(UpdateCheckStatus.ManualUpdateRequired, UpdateEligibility.CheckContext(Context with { Installation = kind }));

    private static UpdateCheckContext Context => new("2.0.0-beta.2+source", UpdateChannel.Preview, "win-x64", 22631, 1, 12, InstallationKind.Managed);
}
