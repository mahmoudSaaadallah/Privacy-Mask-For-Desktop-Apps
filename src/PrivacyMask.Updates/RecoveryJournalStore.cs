namespace PrivacyMask.Updates;

internal enum JournalCheckpoint { TempCreated, TempFlushed, Promoted, TerminalArchived }

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
        var record = RecoveryJson.Read(ManagedInstallLease.ReadBounded(ActivePath, RecoveryRules.JournalLimit), RecoveryRules.JournalLimit,
            RecoveryJson.Context.RecoveryJournal);
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
        var bytes = RecoveryJson.Write(record, RecoveryJson.Context.RecoveryJournal);
        if (bytes.Length > RecoveryRules.JournalLimit) throw new ReleaseValidationException("Recovery journal exceeds its bound.");
        var temporary = Path.Combine(lease.Root, ".update-journal-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var file = ManagedInstallLease.CreatePrivateFile(temporary))
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

    /// <summary>Explicit terminal lifecycle only. Keeps journal and all baselines; never prunes evidence.</summary>
    internal void ArchiveTerminal(string operationId)
    {
        lease.AssertHeld();
        if (!RecoveryRules.IsId(operationId)) throw new ReleaseValidationException("Invalid terminal operation identity.");
        var record = Read();
        if (record is null || record.OperationId != operationId || record.Step is not (RecoveryStep.Committed or RecoveryStep.Recovered))
            throw new ReleaseValidationException("Only the exact terminal operation can be archived.");
        var outcome = new RecoveryRestorer(lease).Recover().Outcome;
        if (outcome != (record.Step == RecoveryStep.Committed ? RecoveryOutcome.Committed : RecoveryOutcome.Recovered))
            throw new ReleaseValidationException("Terminal payload is not consistent.");
        var operation = lease.OperationDirectory(operationId);
        foreach (var (name, expected, maximum) in new[] {
            ("original-app.exe", record.OriginalApp, ManifestRules.MaximumFileSize),
            ("original-settings.json", record.SettingsPrimary, RecoveryRules.SettingsLimit),
            ("original-settings.bak", record.SettingsBackup, RecoveryRules.SettingsLimit) })
            if (RecoveryRestorer.Inspect(Path.Combine(operation, name), maximum) != expected)
                throw new ReleaseValidationException("Terminal baseline is missing or changed.");
        var archive = Path.Combine(operation, "terminal-journal.json");
        StagingWorkspace.AssertSafe(archive);
        if (Path.Exists(archive)) throw new ReleaseValidationException("Terminal archive already exists.");
        lease.AssertHeld();
        if (Read() != record) throw new ReleaseValidationException("Terminal journal changed.");
        // Same-volume rename. Before the move active remains authoritative; afterwards this is
        // retained history ONLY. A future launcher must still validate the normal installed app.
        File.Move(ActivePath, archive, overwrite: false);
        ManagedInstallLease.AssertPrivate(archive);
        checkpoint?.Invoke(JournalCheckpoint.TerminalArchived);
    }
}
