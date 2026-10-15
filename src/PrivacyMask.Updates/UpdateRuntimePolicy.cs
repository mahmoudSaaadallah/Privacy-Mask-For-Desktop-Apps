namespace PrivacyMask.Updates;

/// <summary>Shared compiled trust roots. Never read trust keys from a journal, CLI, environment or download.</summary>
internal static class UpdateRuntimePolicy
{
    // Production key custody/rotation approval remains a separate release gate.
    internal static UpdateManifestVerifier CreateVerifier() => new([]);
}
