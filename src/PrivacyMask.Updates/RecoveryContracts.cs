namespace PrivacyMask.Updates;

// Internal until the launcher owns process exit, trust, consent and readiness gates.
internal enum RecoveryStep { Prepared, Handoff, Replacing, AwaitingReadiness, Committed, RollingBack, Recovered }
internal enum RecoveryOutcome { NoPendingOperation, Committed, Recovered, RepairRequired }
internal sealed record RecoveryResult(RecoveryOutcome Outcome, string? Reason = null);
internal sealed record ManagedInstallMarker(int SchemaVersion, string AppId, string InstallId, int UpdaterProtocol);
internal sealed record RecoveryFile(bool Exists, long Size, string? Sha256);
internal sealed record RecoveryJournal(int SchemaVersion, string InstallId, string OperationId, long Revision,
    RecoveryStep Step, string OriginalVersion, string TargetVersion, RecoveryFile OriginalApp,
    RecoveryFile TargetApp, RecoveryFile SettingsPrimary, RecoveryFile SettingsBackup,
    string ManifestSha256, string SignatureSha256);

internal static class RecoveryRules
{
    internal const int JournalLimit = 16 * 1024;
    internal const long SettingsLimit = 16 * 1024 * 1024;
    internal static bool IsId(string? value) => value is not null && Guid.TryParseExact(value, "N", out var id) && id.ToString("N") == value && id != Guid.Empty;

    internal static void Validate(ManagedInstallMarker marker)
    {
        if (marker.SchemaVersion != 1 || marker.AppId != ManifestRules.AppId || !IsId(marker.InstallId) || marker.UpdaterProtocol != 1)
            throw new ReleaseValidationException("Unsupported managed install marker.");
    }

    internal static void Validate(RecoveryJournal journal)
    {
        if (journal.SchemaVersion != 1 || !IsId(journal.InstallId) || !IsId(journal.OperationId) ||
            journal.Revision is < 1 or > 1_000_000 || !Enum.IsDefined(journal.Step) ||
            journal.OriginalVersion is null || journal.TargetVersion is null ||
            !ManifestRules.IsVersion(journal.OriginalVersion) || !ManifestRules.IsVersion(journal.TargetVersion) ||
            !SemanticVersion.TryParse(journal.TargetVersion, out var target) || !SemanticVersion.TryParse(journal.OriginalVersion, out var original) ||
            target.CompareTo(original) <= 0 ||
            journal.ManifestSha256 is null || journal.SignatureSha256 is null ||
            !ManifestRules.IsHash(journal.ManifestSha256) || !ManifestRules.IsHash(journal.SignatureSha256))
            throw new ReleaseValidationException("Unsupported recovery journal identity.");
        ValidateFile(journal.OriginalApp, ManifestRules.MaximumFileSize, required: true);
        ValidateFile(journal.TargetApp, ManifestRules.MaximumFileSize, required: true);
        ValidateFile(journal.SettingsPrimary, SettingsLimit);
        ValidateFile(journal.SettingsBackup, SettingsLimit);
    }

    private static void ValidateFile(RecoveryFile? file, long maximum, bool required = false)
    {
        if (file is null || (required && !file.Exists) ||
            (file.Exists ? file.Size < 0 || file.Size > maximum || (required && file.Size == 0) ||
                file.Sha256 is null || !ManifestRules.IsHash(file.Sha256) : file.Size != 0 || file.Sha256 is not null))
            throw new ReleaseValidationException("Invalid recovery file snapshot.");
    }

    internal static bool CanTransition(RecoveryStep from, RecoveryStep to) => (from, to) switch
    {
        (RecoveryStep.Prepared, RecoveryStep.Handoff) or
        (RecoveryStep.Handoff, RecoveryStep.Replacing) or
        (RecoveryStep.Replacing, RecoveryStep.AwaitingReadiness) or
        (RecoveryStep.AwaitingReadiness, RecoveryStep.Committed) or
        (RecoveryStep.RollingBack, RecoveryStep.Recovered) => true,
        (_, RecoveryStep.RollingBack) when from is not (RecoveryStep.Committed or RecoveryStep.Recovered or RecoveryStep.RollingBack) => true,
        _ => false
    };
}
