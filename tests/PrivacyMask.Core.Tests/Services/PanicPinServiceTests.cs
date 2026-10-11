using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class PanicPinServiceTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("01234567")]
    public void CreateAndVerify_AcceptsValidPins(string pin)
    {
        var credential = PanicPinService.Create(pin);

        Assert.True(PanicPinService.IsCredentialValid(credential));
        Assert.True(PanicPinService.Verify(pin, credential));
        Assert.False(PanicPinService.Verify("9999", credential));
        Assert.DoesNotContain(pin, credential.Hash, StringComparison.Ordinal);
        Assert.DoesNotContain(pin, credential.Salt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("12a4")]
    [InlineData("١٢٣٤")]
    public void IsValidPin_RejectsNonConformingValues(string pin)
    {
        Assert.False(PanicPinService.IsValidPin(pin));
    }

    [Fact]
    public void Verify_RejectsMalformedOrExcessiveCredentials()
    {
        var malformed = new PanicPinCredential
        {
            Iterations = int.MaxValue,
            Salt = "not-base64",
            Hash = "not-base64",
        };

        Assert.False(PanicPinService.IsCredentialValid(malformed));
        Assert.False(PanicPinService.Verify("1234", malformed));
    }

    [Fact]
    public void Create_UsesUniqueSaltForTheSamePin()
    {
        var first = PanicPinService.Create("1234");
        var second = PanicPinService.Create("1234");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }
}
