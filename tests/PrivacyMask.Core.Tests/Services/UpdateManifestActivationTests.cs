using System.Security.Cryptography;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateManifestActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOperationFlagCannotReplaceMissingTrust(bool enabled)
    {
        var verifier = new UpdateManifestVerifier([], updateOperationsEnabled: enabled);
        Assert.False(verifier.HasTrustedKeys);
        Assert.False(verifier.IsConfigured);
        Assert.Throws<ReleaseValidationException>(() => verifier.Verify([], []));
    }

    [Fact]
    public void DisabledOperationsDoNotSkipPublicKeyValidation()
    {
        Assert.Throws<FormatException>(() => new UpdateManifestVerifier([new("bad-key", "invalid")], false));
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ReleaseValidationException>(() => new UpdateManifestVerifier(
            [new("bad-key", Convert.ToBase64String(wrongCurve.ExportSubjectPublicKeyInfo()))], false));
    }

    [Fact]
    public void RuntimeGateBlocksEvenAnOtherwiseValidFixtureSignature()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Release();
        var manifest = fixture.Handler.Assets[101];
        var envelope = fixture.Handler.Assets[102];
        var pin = new TrustedReleaseKey("test-key", Convert.ToBase64String(fixture.Key.ExportSubjectPublicKeyInfo()));
        Assert.Equal("2.0.0-beta.3", new UpdateManifestVerifier([pin]).Verify(manifest, envelope).Version);
        var disabled = new UpdateManifestVerifier([pin], updateOperationsEnabled: false);
        Assert.True(disabled.HasTrustedKeys);
        Assert.False(disabled.IsConfigured);
        Assert.Throws<ReleaseValidationException>(() => disabled.Verify(manifest, envelope));
    }
}
