using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrivacyMask.ReleaseTool;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ReleaseManifestTests
{
    [Fact]
    public void ExactByteSignatureIsP1363AndAuthenticatesWithPinnedKey()
    {
        using var fixture = new SigningFixture();
        var bytes = ContractJson.Write(fixture.Manifest);
        var envelope = ManifestSigning.Sign(bytes, fixture.Pem, "test-key", fixture.Policy);
        var signatureBytes = Convert.FromBase64String(envelope.Signature);
        Assert.Equal(64, signatureBytes.Length);
        Assert.True(fixture.Key.VerifyData(bytes, signatureBytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Equal(fixture.Manifest.Version,
            ManifestSigning.Verify(bytes, ContractJson.Write(envelope), fixture.Policy).Version);
        Assert.Throws<ReleaseValidationException>(() =>
            ManifestSigning.Verify([.. bytes, (byte)'\n'], ContractJson.Write(envelope), fixture.Policy));
    }

    [Fact]
    public void SigningRequiresPrivateKeyMatchingReviewedPublicKey()
    {
        using var fixture = new SigningFixture();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.Sign(
            ContractJson.Write(fixture.Manifest), other.ExportPkcs8PrivateKeyPem(), "test-key", fixture.Policy));
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.Sign(
            ContractJson.Write(fixture.Manifest), fixture.Pem, "unknown-key", fixture.Policy));
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.Sign(
            ContractJson.Write(fixture.Manifest), fixture.Pem + fixture.Pem, "test-key", fixture.Policy));
    }

    [Fact]
    public void RejectsUnconfiguredPolicyAndWrongCurve()
    {
        using var fixture = new SigningFixture();
        Assert.Throws<ReleaseValidationException>(() => ManifestRules.Validate(fixture.Policy with { MinimumWindowsBuild = null }));
        Assert.Throws<ReleaseValidationException>(() => ManifestRules.Validate(fixture.Policy with { TrustedKeys = [] }));
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.ImportPublicKey(
            Convert.ToBase64String(wrongCurve.ExportSubjectPublicKeyInfo())));
        Assert.Throws<ReleaseValidationException>(() => ManifestRules.Validate(fixture.Policy with
        { TrustedKeys = [fixture.Policy.TrustedKeys[0], fixture.Policy.TrustedKeys[0]] }));
    }

    [Theory]
    [InlineData("{\"algorithm\":\"none\",\"keyId\":\"test-key\",\"signature\":\"AA==\"}")]
    [InlineData("{\"algorithm\":\"ECDSA-P256-SHA256\",\"keyId\":\"unknown-key\",\"signature\":\"AA==\"}")]
    [InlineData("{\"algorithm\":\"ECDSA-P256-SHA256\",\"keyId\":\"test-key\",\"signature\":\"AA==\",\"publicKey\":\"untrusted\"}")]
    public void RejectsUntrustedSignatureEnvelopes(string envelope)
    {
        using var fixture = new SigningFixture();
        Assert.ThrowsAny<Exception>(() => ManifestSigning.Verify(
            ContractJson.Write(fixture.Manifest), Encoding.UTF8.GetBytes(envelope), fixture.Policy));
    }

    [Fact]
    public void RejectsNoncanonicalBase64AndDerSignatures()
    {
        using var fixture = new SigningFixture();
        var bytes = ContractJson.Write(fixture.Manifest);
        var envelope = ManifestSigning.Sign(bytes, fixture.Pem, "test-key", fixture.Policy);
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.Verify(bytes,
            ContractJson.Write(envelope with { Signature = envelope.Signature + "\n" }), fixture.Policy));
        var der = fixture.Key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.Verify(bytes,
            ContractJson.Write(envelope with { Signature = Convert.ToBase64String(der) }), fixture.Policy));
    }

    [Theory]
    [InlineData("{\"algorithm\":\"x\",\"algorithm\":\"y\"}")]
    [InlineData("{\"nested\":{\"key\":1,\"key\":2}}")]
    [InlineData("{} {}")]
    [InlineData("{\"SchemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":null}")]
    public void JsonReaderRejectsDuplicatesUnknownPropertiesTrailingValuesAndMissingFields(string json)
    {
        Assert.ThrowsAny<Exception>(() => ContractJson.Read<UpdateManifest>(Encoding.UTF8.GetBytes(json), ManifestRules.ManifestLimit));
    }

    [Fact]
    public void JsonReaderRejectsBomInvalidUtf8AndLimits()
    {
        Assert.ThrowsAny<Exception>(() => ContractJson.Read<UpdateManifest>([0xef, 0xbb, 0xbf, (byte)'{', (byte)'}'], 100));
        Assert.ThrowsAny<Exception>(() => ContractJson.Read<UpdateManifest>([0xff, 0xfe], 100));
        Assert.Throws<ReleaseValidationException>(() => ContractJson.Read<UpdateManifest>(new byte[101], 100));
    }

    [Theory]
    [InlineData("2.0.0-beta.3", true)]
    [InlineData("2.0.0-beta.10", true)]
    [InlineData("2.0.0-rc.1", true)]
    [InlineData("2.0.0", true)]
    [InlineData("02.0.0", false)]
    [InlineData("2.0.0-beta.01", false)]
    [InlineData("2.0.0+build", false)]
    [InlineData("2.0.0\n", false)]
    public void StrictProductIdentity(string version, bool valid) => Assert.Equal(valid, ManifestRules.IsVersion(version));

    [Fact]
    public void RejectsSemanticallyMismatchedMetadata()
    {
        using var fixture = new SigningFixture();
        var original = fixture.Manifest;
        UpdateManifest[] invalid = [
            original with { Tag = "v9.0.0" },
            original with { Channel = "stable" },
            original with { Repository = "other/repo" },
            original with { Runtime = "win-arm64" },
            original with { MinUpdaterProtocol = 0 },
            original with { Package = original.Package with { AssetName = "wrong.zip" } },
            original with { Files = [original.Files[0] with { Path = "../PrivacyMask.App.exe" }] },
            original with { Files = [original.Files[0], original.Files[0]] },
            original with { SettingsCompatibility = new(20, 12, 12) }
        ];
        foreach (var manifest in invalid) Assert.Throws<ReleaseValidationException>(() => ManifestRules.Validate(manifest));
    }

    [Fact]
    public void AuthenticButMalformedManifestStillFailsValidation()
    {
        using var fixture = new SigningFixture();
        var raw = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ContractJson.Write(fixture.Manifest))
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"));
        var envelope = new SignatureEnvelope(ManifestRules.Algorithm, "test-key", Convert.ToBase64String(
            fixture.Key.SignData(raw, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        Assert.Throws<ReleaseValidationException>(() => ManifestSigning.Verify(raw, ContractJson.Write(envelope), fixture.Policy));
    }

    private sealed class SigningFixture : IDisposable
    {
        public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Pem => Key.ExportPkcs8PrivateKeyPem();
        public ReleasePolicy Policy => new(1, 19041, 1, new(1, 12, 12),
            [new("test-key", Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo()))]);
        public UpdateManifest Manifest => new(1, ManifestRules.AppId, ManifestRules.Repository,
            "2.0.0-beta.3", "v2.0.0-beta.3", new string('1', 40), "preview", "win-x64", 19041, 1,
            new(ManifestRules.ArchiveName("2.0.0-beta.3"), 1024, new string('a', 64)),
            [new("PrivacyMask.App.exe", 2048, new string('b', 64))], new(1, 12, 12));
        public void Dispose() => Key.Dispose();
    }
}
