using System.Security.Cryptography;
using PrivacyMask.Updates;

namespace PrivacyMask.ReleaseTool;

public static class ManifestSigning
{
    public static ECDsa ImportPublicKey(string spkiBase64)
    {
        return UpdateManifestVerifier.ImportPublicKey(spkiBase64);
    }

    private static void AssertP256(ECDsa key)
    {
        if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new ReleaseValidationException("Update keys must use the NIST P-256 curve.");
    }

    public static SignatureEnvelope Sign(byte[] manifestBytes, string privateKeyPem, string keyId, ReleasePolicy policy)
    {
        ManifestRules.Validate(policy);
        var manifest = ContractJson.Read<UpdateManifest>(manifestBytes, ManifestRules.ManifestLimit);
        ManifestRules.Validate(manifest);
        var pinned = policy.TrustedKeys.SingleOrDefault(key => key.KeyId == keyId)
            ?? throw new ReleaseValidationException("Signing key ID is not pinned in release policy.");
        using var key = ECDsa.Create();
        // A private key is supplied in process environment, never in argv or output.
        var pemSpan = privateKeyPem.AsSpan();
        if (!PemEncoding.TryFind(pemSpan, out var fields) || pemSpan[fields.Label] is not "PRIVATE KEY" ||
            !pemSpan[..fields.Location.Start.GetOffset(pemSpan.Length)].Trim().IsEmpty ||
            !pemSpan[fields.Location.End.GetOffset(pemSpan.Length)..].Trim().IsEmpty)
            throw new ReleaseValidationException("Expected one PKCS#8 private PEM key.");
        key.ImportFromPem(pemSpan);
        AssertP256(key);
        using var expected = ImportPublicKey(pinned.PublicKeySpkiBase64);
        if (!CryptographicOperations.FixedTimeEquals(key.ExportSubjectPublicKeyInfo(), expected.ExportSubjectPublicKeyInfo()))
            throw new ReleaseValidationException("Private key does not match the reviewed pinned public key.");
        return new(ManifestRules.Algorithm, keyId, Convert.ToBase64String(key.SignData(manifestBytes,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }

    public static UpdateManifest Verify(byte[] manifestBytes, byte[] envelopeBytes, ReleasePolicy policy)
    {
        ManifestRules.Validate(policy);
        var manifest = new UpdateManifestVerifier(policy.TrustedKeys).Verify(manifestBytes, envelopeBytes);
        if (manifest.MinWindowsBuild != policy.MinimumWindowsBuild || manifest.MinUpdaterProtocol != policy.MinimumUpdaterProtocol ||
            manifest.SettingsCompatibility != policy.SettingsCompatibility)
            throw new ReleaseValidationException("Manifest does not match the reviewed release policy.");
        return manifest;
    }
}
