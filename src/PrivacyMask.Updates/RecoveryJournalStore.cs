namespace PrivacyMask.Updates;

internal enum JournalCheckpoint { TempCreated, TempFlushed, Promoted }

/// <summary>Atomic active record; unpromoted scratch is evidence, never a recovery command.</summary>
internal sealed class RecoveryJournalStore(ManagedInstallLease lease, Action<JournalCheckpoint>? checkpoint = null)
{
    private string ActivePath => Path.Combine(lease.Root, "update-journal.json");

    internal RecoveryJournal? Read()
    {
        lease.AssertHeld();
        StagingWorkspace.AssertSafe(ActivePath);
        // GetAttributes distinguishes absence from denied access and a directory impersonating a file.
        try { _ = File.GetAttributes(ActivePath); }
        catch (FileNotFoundException) { return null; }
        ManagedInstallLease.AssertPrivate(ActivePath);
        var record = ContractJson.Read<RecoveryJournal>(ManagedInstallLease.ReadBounded(ActivePath, RecoveryRules.JournalLimit), RecoveryRules.JournalLimit);
        RecoveryRules.Validate(record);
        if (record.InstallId != lease.InstallId) throw new ReleaseValidationException("Recovery record belongs to a different installation.");
        return record;
    }

    internal void Write(RecoveryJournal record)
    {
        lease.AssertHeld();
        RecoveryRules.Validate(record);
        if (record.InstallId != lease.InstallId) throw new ReleaseValidationException("Recovery install identity mismatch.");
        _ = lease.OperationDirectory(record.OperationId);
        var previous = Read();
        if (previous is null)
        {
            if (record.Revision != 1 || record.Step != RecoveryStep.Prepared)
                throw new ReleaseValidationException("First journal record must be prepared revision one.");
        }
        else if (record.Revision != previous.Revision + 1 || !RecoveryRules.CanTransition(previous.Step, record.Step) ||
            record with { Revision = previous.Revision, Step = previous.Step } != previous)
            throw new ReleaseValidationException("Invalid recovery transition or changed operation identity.");
        var bytes = ContractJson.Write(record);
        if (bytes.Length > RecoveryRules.JournalLimit) throw new ReleaseValidationException("Recovery journal exceeds its bound.");
        var temporary = Path.Combine(lease.Root, ".update-journal-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            checkpoint?.Invoke(JournalCheckpoint.TempCreated);
            file.Write(bytes);
            file.Flush(flushToDisk: true);
            checkpoint?.Invoke(JournalCheckpoint.TempFlushed);
        }
        ManagedInstallLease.AssertPrivate(temporary);
        lease.AssertHeld();
        StagingWorkspace.AssertSafe(ActivePath);
        if (previous is null) File.Move(temporary, ActivePath, overwrite: false);
        else File.Replace(temporary, ActivePath, destinationBackupFileName: null, ignoreMetadataErrors: false);
        checkpoint?.Invoke(JournalCheckpoint.Promoted);
    }
}
