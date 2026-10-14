namespace PrivacyMask.Updates;

public enum UpdateChannel { Stable, Preview }
public enum InstallationKind { Unknown, Source, Portable, LegacyInstalled, Managed }
public enum UpdateCheckStatus
{
    Available, UpToDate, NoCompatibleUpdate, UnableToCheck, Incomplete,
    RateLimited, NotConfigured, ManualUpdateRequired, InProgress, InvalidContext
}

/// <summary>Host facts, not proof of filesystem ownership. Revalidate before apply.</summary>
public sealed record UpdateCheckContext(string InstalledVersion, UpdateChannel Channel,
    string Runtime, int WindowsBuild, int UpdaterProtocol, int SettingsSchema, InstallationKind Installation);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateCandidate? Candidate = null,
    DateTimeOffset? RetryAt = null, int RejectedReleases = 0, string? Reason = null);

/// <summary>Authenticated discovery snapshot, not permission to install. No ZIP downloaded.</summary>
public sealed record UpdateCandidate(long ReleaseId, long PackageAssetId, Uri ReleasePage,
    Uri PackageApiUrl, UpdateManifest Manifest, ReadOnlyMemory<byte> ManifestBytes,
    ReadOnlyMemory<byte> SignatureBytes, string ReleaseNotes);

public static class UpdateEligibility
{
    public static UpdateCheckStatus? CheckContext(UpdateCheckContext context)
    {
        if (!SemanticVersion.TryParse(context.InstalledVersion, out _) ||
            !Enum.IsDefined(context.Channel) || !Enum.IsDefined(context.Installation))
            return UpdateCheckStatus.InvalidContext;
        if (context.Installation != InstallationKind.Managed || context.Runtime != "win-x64")
            return UpdateCheckStatus.ManualUpdateRequired;
        if (context.WindowsBuild <= 0 || context.SettingsSchema <= 0 || context.UpdaterProtocol <= 0)
            return UpdateCheckStatus.InvalidContext;
        return null;
    }

    public static bool IsNewerForChannel(string productVersion, bool prerelease, UpdateCheckContext context)
    {
        if (!ManifestRules.IsVersion(productVersion) || !SemanticVersion.TryParse(productVersion, out var candidate) ||
            !SemanticVersion.TryParse(context.InstalledVersion, out var current) ||
            candidate.IsPrerelease != prerelease || (context.Channel == UpdateChannel.Stable && prerelease)) return false;
        return candidate.CompareTo(current) > 0;
    }

    public static bool IsCompatible(UpdateManifest manifest, UpdateCheckContext context) =>
        manifest.Runtime == context.Runtime && manifest.MinWindowsBuild <= context.WindowsBuild &&
        manifest.MinUpdaterProtocol <= context.UpdaterProtocol &&
        manifest.SettingsCompatibility.MinimumReadableSchema <= context.SettingsSchema &&
        manifest.SettingsCompatibility.MaximumReadableSchema >= context.SettingsSchema;
}
