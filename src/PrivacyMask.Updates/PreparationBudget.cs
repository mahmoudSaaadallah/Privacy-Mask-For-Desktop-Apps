namespace PrivacyMask.Updates;

internal static class PreparationBudget
{
    internal const long Margin = 64L * 1024 * 1024;
    internal const long MetadataReserve = 1024 * 1024;

    // Existing stage/app/data already occupy disk and are not counted as free space.
    // Reserve new retained copies + forward/rollback scratch + worst-case bounded settings growth.
    internal static long Required(RecoveryFile app, RecoveryFile primary, RecoveryFile secondary, RecoveryFile target, bool copiesCreated = false)
    {
        var baseline = checked(app.Size + primary.Size + secondary.Size);
        return checked((copiesCreated ? 1 : 2) * (baseline + target.Size) +
            2 * RecoveryRules.SettingsLimit + Margin + MetadataReserve);
    }
}
