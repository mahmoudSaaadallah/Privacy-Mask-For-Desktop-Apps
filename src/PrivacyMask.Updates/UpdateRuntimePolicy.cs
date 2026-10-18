namespace PrivacyMask.Updates;

/// <summary>Shared compiled trust roots. Never read trust keys from a journal, CLI, environment or download.</summary>
internal static class UpdateRuntimePolicy
{
    // Owner-approved public SPKI, SHA-256:
    // 62fb7fb06206ad60c725754f525b4eaa6d2cb6645a12a3b8a008c736f534724d
    // Return a fresh snapshot: callers cannot change the compiled trust registry.
    internal static IReadOnlyList<TrustedReleaseKey> TrustedKeys =>
    [
        new("release-key-1", "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAET51tmvseX6BRpk7j0M+xCnvYyMogUFwUBHYBGaw6MZeoA26Dm/GgGX/bGHr9PVej4XPOR4QTUnoiKlALmTnZ4A==")
    ];

    // Public pinning does NOT authorize network/install activation. Changing this
    // requires a later reviewed delivery after environment/bootstrap/OS gates.
    // No settings, environment variable, CLI argument or downloaded policy override.
    internal static bool UpdateOperationsEnabled => false;

    internal static UpdateManifestVerifier CreateVerifier() =>
        new(TrustedKeys, updateOperationsEnabled: UpdateOperationsEnabled);
}
