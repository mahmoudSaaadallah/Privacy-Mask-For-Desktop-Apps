namespace PrivacyMask.Updates;

internal enum RetentionOutcome { Completed, Partial, Deferred }
internal sealed record RetentionResult(RetentionOutcome Outcome, int RemovedDirectories = 0, long RemovedBytes = 0);
internal enum RetentionCheckpoint { Quarantined, FileRemoved, DirectoryRemoved }

/// <summary>Bounds both scans and admission. Unknown or unresolved evidence consumes capacity rather than being erased.</summary>
internal static class UpdateStoragePolicy
{
    internal const int MaximumCatalogEntries = 64;
    internal const int MaximumFiles = 32;
    internal const int MaximumOperations = 8;
    internal const int MaximumStages = 4;
    internal const int MaximumQuarantined = 8;
    internal const int MaximumRemovalsPerRun = 4;
    internal static readonly TimeSpan StageLifetime = TimeSpan.FromHours(24);

    internal static string[] Catalog(ManagedInstallLease lease, string area)
    {
        lease.AssertHeld();
        if (area is not ("update-operations" or "update-staging" or "update-pruning"))
            throw new ReleaseValidationException("Unknown update storage area.");
        var path = Path.Combine(lease.Root, area);
        StagingWorkspace.AssertSafe(path);
        try { _ = File.GetAttributes(path); }
        catch (FileNotFoundException) { return []; }
        ManagedInstallLease.AssertPrivate(path);
        if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
            throw new ReleaseValidationException("Update storage is not a directory.");
        var entries = Directory.EnumerateFileSystemEntries(path).Take(MaximumCatalogEntries + 1).ToArray();
        if (entries.Length > MaximumCatalogEntries)
            throw new ReleaseValidationException("Update storage scan limit reached.");
        return entries;
    }

    internal static void RequireCapacity(ManagedInstallLease lease, bool creatingStage)
    {
        // Even foreign names count. Do not keep writing indefinitely around evidence we cannot safely remove.
        if (Catalog(lease, "update-operations").Length >= MaximumOperations ||
            Catalog(lease, "update-pruning").Length >= MaximumQuarantined ||
            (creatingStage && Catalog(lease, "update-staging").Length >= MaximumStages))
            throw new ReleaseValidationException("Update storage needs attention before another attempt.");
    }

    internal static bool TryId(string path, string prefix, out string id)
    {
        var name = Path.GetFileName(path);
        id = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : "";
        return RecoveryRules.IsId(id);
    }
}
