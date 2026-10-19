using System.IO;
using System.Security.Cryptography;
using PrivacyMask.ReleaseTool;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateRuntimePolicyTests
{
    private const string ApprovedFingerprint = "62fb7fb06206ad60c725754f525b4eaa6d2cb6645a12a3b8a008c736f534724d";

    [Fact]
    public void CompiledPublicPinMatchesOwnerApprovedIdentityAndFingerprint()
    {
        var pin = Assert.Single(UpdateRuntimePolicy.TrustedKeys);
        Assert.Equal("release-key-1", pin.KeyId);
        var bytes = Convert.FromBase64String(pin.PublicKeySpkiBase64);
        Assert.Equal(ApprovedFingerprint, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using var key = UpdateManifestVerifier.ImportPublicKey(pin.PublicKeySpkiBase64);
        Assert.Equal(256, key.KeySize);
        Assert.Equal("1.2.840.10045.3.1.7", key.ExportParameters(false).Curve.Oid.Value);
        Assert.Equal(bytes, key.ExportSubjectPublicKeyInfo());
        Assert.Throws<CryptographicException>(() => key.ExportPkcs8PrivateKey());
    }

    [Fact]
    public void PublisherAndSharedAppLauncherPinsCannotDrift()
    {
        var policy = ReadPublisherPolicy();
        Assert.Equal(UpdateRuntimePolicy.TrustedKeys.ToArray(), policy.TrustedKeys.ToArray());
        Assert.Single(policy.TrustedKeys);
        Assert.Equal(UpdateRuntimePolicy.MinimumAutomaticWindowsBuild, policy.MinimumWindowsBuild);
        // A technical Preview floor is not a completed Windows qualification report.
        ManifestRules.Validate(policy);
    }

    [Fact]
    public void CallerCannotReplaceTheCompiledRegistryThroughASnapshot()
    {
        var snapshot = UpdateRuntimePolicy.TrustedKeys;
        Assert.NotSame(snapshot, UpdateRuntimePolicy.TrustedKeys);
        if (snapshot is TrustedReleaseKey[] array) array[0] = new("foreign-key", "invalid");
        Assert.Equal("release-key-1", Assert.Single(UpdateRuntimePolicy.TrustedKeys).KeyId);
    }

    [Fact]
    public void ShippedPreviewVerifierEnablesOnlyPinnedAuthenticatedOperations()
    {
        var verifier = UpdateRuntimePolicy.CreateVerifier();
        Assert.True(verifier.HasTrustedKeys);
        Assert.True(UpdateRuntimePolicy.UpdateOperationsEnabled);
        Assert.True(verifier.IsConfigured);
        Assert.Throws<ReleaseValidationException>(() => verifier.Verify([], []));
    }

    [Fact]
    public async Task PinnedButUnapprovedRuntimeCannotSendHttp()
    {
        using var fixture = new UpdateDiscoveryFixture();
        using var client = new GitHubReleaseClient(new(UpdateRuntimePolicy.TrustedKeys, false), fixture.Clock, fixture.Handler);
        Assert.Equal(UpdateCheckStatus.NotConfigured, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task PinnedButUnapprovedNativeFactoryDoesNotInspectHistoryRoot()
    {
        using var client = GitHubReleaseClient.ForManagedApply(new(UpdateRuntimePolicy.TrustedKeys, false), "\0invalid");
        var context = new UpdateCheckContext("2.0.0-beta.12", UpdateChannel.Preview, "win-x64", 22631, 1, 13, InstallationKind.Managed);
        Assert.Equal(UpdateCheckStatus.NotConfigured, (await client.CheckAsync(context)).Status);
    }

    [Fact]
    public async Task ActivatedProductionStagingRejectsForeignSignatureBeforeHttpOrStagingIo()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Release();
        var bytes = fixture.Handler.Assets[101];
        var signature = fixture.Handler.Assets[102];
        var manifest = ContractJson.Read<UpdateManifest>(bytes, ManifestRules.ManifestLimit);
        var candidate = new UpdateCandidate(10, 103, GitHubUrls.ReleasePage(manifest.Tag), GitHubUrls.AssetApi(103),
            manifest, bytes, signature, "");
        using var stager = new UpdateStager(UpdateRuntimePolicy.CreateVerifier(), fixture.Clock, fixture.Handler);
        var error = await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(candidate, fixture.Context, "\0invalid"));
        Assert.Equal("update-validation-failed", error.Reason);
        Assert.False(error.CleanupIncomplete);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("release-key-1")]
    [InlineData("unknown-key")]
    public void ProductionPinRejectsAnEphemeralSignatureEvenWithClaimedOwnerKeyId(string claimedKeyId)
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Release();
        var manifest = fixture.Handler.Assets[101];
        var signature = fixture.Key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var envelope = ContractJson.Write(new SignatureEnvelope(ManifestRules.Algorithm, claimedKeyId, Convert.ToBase64String(signature)));
        // Public-only offline verification; never possess or request the owner's private key.
        var verifier = new UpdateManifestVerifier(UpdateRuntimePolicy.TrustedKeys);
        Assert.Throws<ReleaseValidationException>(() => verifier.Verify(manifest, envelope));
    }

    [Fact]
    public void PublisherCannotSignAsOwnerUsingAFixturePrivateKey()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Release();
        var policy = ReadPublisherPolicy() with { MinimumWindowsBuild = 19041 }; // Fixture only, never write policy.
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.Sign(
            fixture.Handler.Assets[101], fixture.Key.ExportPkcs8PrivateKeyPem(), "release-key-1", policy));
    }

    private static ReleasePolicy ReadPublisherPolicy()
    {
        using var stream = typeof(UpdateRuntimePolicyTests).Assembly.GetManifestResourceStream("PrivacyMask.Tests.ReleasePolicy.json")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return ContractJson.Read<ReleasePolicy>(bytes.ToArray(), 64 * 1024);
    }
}
