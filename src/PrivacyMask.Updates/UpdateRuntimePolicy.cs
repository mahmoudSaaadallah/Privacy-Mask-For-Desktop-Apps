using System.Reflection;

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

    // Owner-authorized Preview experiment, not stable qualification. This is a
    // Windows 11 compatibility filter, not evidence that every build was tested.
    internal const int MinimumAutomaticWindowsBuild = 22000;
    internal static string ProductVersion { get; } = typeof(UpdateRuntimePolicy).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    internal static bool UpdateOperationsEnabled => IsPreviewRuntimeVersion(ProductVersion);
    internal static bool IsPreviewRuntimeVersion(string version) =>
        SemanticVersion.TryParse(version, out var parsed) && parsed.IsPrerelease;
    internal static bool SupportsAutomaticUpdates(string version, int windowsBuild) =>
        IsPreviewRuntimeVersion(version) && windowsBuild >= MinimumAutomaticWindowsBuild;

    // Exact-version exception: B (beta.15) and later builds default back to daily
    // without a saved setting, environment flag or downloaded policy override.
    internal static TimeSpan SuccessfulCheckInterval => GetSuccessfulCheckInterval(ProductVersion);
    internal static TimeSpan GetSuccessfulCheckInterval(string version) =>
        SemanticVersion.TryParse(version, out var parsed) && parsed.ProductVersion == "2.0.0-beta.14"
            ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(24);

    internal static UpdateManifestVerifier CreateVerifier() =>
        new(TrustedKeys, updateOperationsEnabled: UpdateOperationsEnabled);
}
