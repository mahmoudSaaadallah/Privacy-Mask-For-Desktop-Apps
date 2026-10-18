using System.Security.Cryptography;

namespace PrivacyMask.Updates;

/// <summary>Public-key-only trust. Never discovers keys from remote metadata.</summary>
public sealed class UpdateManifestVerifier
{
    private readonly TrustedReleaseKey[] _keys;
    private readonly bool _updateOperationsEnabled;
    internal bool HasTrustedKeys => _keys.Length != 0;
    /// <summary>Both pinned trust and permission to use update operations are required.</summary>
    public bool IsConfigured => _updateOperationsEnabled && HasTrustedKeys;

    public UpdateManifestVerifier(IEnumerable<TrustedReleaseKey> pinnedKeys)
        : this(pinnedKeys, updateOperationsEnabled: true) { }

    // A public pin is not release/qualification approval. Keep this compiled
    // runtime gate inside the verifier so every existing consumer fails closed.
    internal UpdateManifestVerifier(IEnumerable<TrustedReleaseKey> pinnedKeys, bool updateOperationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(pinnedKeys);
        _updateOperationsEnabled = updateOperationsEnabled;
        _keys = pinnedKeys.Take(17).ToArray();
        if (_keys.Length > 16) throw new ReleaseValidationException("Too many pinned update keys.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in _keys)
        {
            if (key is null || !ManifestRules.IsKeyId(key.KeyId) || !ids.Add(key.KeyId))
                throw new ReleaseValidationException("Invalid or duplicate pinned key ID.");
            using var imported = ImportPublicKey(key.PublicKeySpkiBase64);
        }
    }

    public static ECDsa ImportPublicKey(string spkiBase64)
    {
        var bytes = Convert.FromBase64String(spkiBase64);
        if (Convert.ToBase64String(bytes) != spkiBase64)
            throw new ReleaseValidationException("Pinned public key must use canonical Base64.");
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(bytes, out var consumed);
            if (consumed != bytes.Length || key.KeySize != 256 ||
                key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
                throw new ReleaseValidationException("Expected one NIST P-256 public key.");
            return key;
        }
        catch { key.Dispose(); throw; }
    }

    public UpdateManifest Verify(byte[] manifestBytes, byte[] envelopeBytes)
    {
        if (!IsConfigured) throw new ReleaseValidationException("Signed update operations are not configured.");
        if (manifestBytes.Length is 0 or > ManifestRules.ManifestLimit)
            throw new ReleaseValidationException("Invalid manifest length.");
        var envelope = ContractJson.Read<SignatureEnvelope>(envelopeBytes, ManifestRules.SignatureLimit);
        if (envelope.Algorithm != ManifestRules.Algorithm || !ManifestRules.IsKeyId(envelope.KeyId))
            throw new ReleaseValidationException("Unsupported signature algorithm or key ID.");
        var pinned = _keys.SingleOrDefault(key => key.KeyId == envelope.KeyId)
            ?? throw new ReleaseValidationException("Signature key is not pinned.");
        var signature = Convert.FromBase64String(envelope.Signature);
        if (signature.Length != 64 || Convert.ToBase64String(signature) != envelope.Signature)
            throw new ReleaseValidationException("Signature must use canonical Base64 P1363 format.");
        using var key = ImportPublicKey(pinned.PublicKeySpkiBase64);
        if (!key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new ReleaseValidationException("Manifest authentication failed.");
        var manifest = ContractJson.Read<UpdateManifest>(manifestBytes, ManifestRules.ManifestLimit);
        ManifestRules.Validate(manifest);
        return manifest;
    }
}
