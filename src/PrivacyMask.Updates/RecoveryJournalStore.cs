namespace PrivacyMask.Updates;

internal enum JournalCheckpoint { TempCreated, TempFlushed, Promoted, TerminalArchived }

/// <summary>Atomic active record; unpromoted scratch is evidence, never a recovery command.</summary>
internal sealed class RecoveryJournalStore(ManagedInstallLease lease, Action<JournalCheckpoint>? checkpoint = null)
{
    internal const string TemporaryFileName = ".update-journal.tmp";
    internal const string UnpromotedFileName = "unpromoted-journal.json";
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

    /// <summary>Only before authenticated exit: retire an unchanged Prepared baseline without restoring files.
    /// Caller holds frozen writers and has confirmed its exact worker has stopped.</summary>
    internal void AbandonPrepared(string operationId)
    {
        var record = Read();
        if (record is null || record.OperationId != operationId || record.Step != RecoveryStep.Prepared)
            throw new ReleaseValidationException("Only the exact unapplied operation can be abandoned.");
        var operation = lease.OperationDirectory(operationId);
        foreach (var (installed, retained, expected, maximum) in new[] {
            (Path.Combine(lease.Root, "app", LauncherBoot.ApplicationFileName), "original-app.exe", record.OriginalApp, ManifestRules.MaximumFileSize),
            (Path.Combine(lease.Root, "settings.v1.json"), "original-settings.json", record.SettingsPrimary, RecoveryRules.SettingsLimit),
            (Path.Combine(lease.Root, "settings.v1.json.bak"), "original-settings.bak", record.SettingsBackup, RecoveryRules.SettingsLimit) })
            if (RecoveryRestorer.Inspect(installed, maximum) != expected ||
                RecoveryRestorer.Inspect(Path.Combine(operation, retained), maximum) != expected)
                throw new ReleaseValidationException("Unapplied operation baseline changed.");
        // Promotion must finish before any writer is resumed. A crash between these records
        // leaves boot recovery authoritative while no new in-process writes have been admitted.
        record = record with { Step = RecoveryStep.RollingBack, Revision = record.Revision + 1 };
        Write(record);
        Write(record with { Step = RecoveryStep.Recovered, Revision = record.Revision + 1 });
        ArchiveTerminal(operationId);
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
        var temporary = Path.Combine(lease.Root, TemporaryFileName);
        PreserveInterruptedWrite(temporary, record.OperationId);
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

    private void PreserveInterruptedWrite(string temporary, string operationId)
    {
        StagingWorkspace.AssertSafe(temporary);
        try { _ = File.GetAttributes(temporary); }
        catch (FileNotFoundException) { return; }
        var pending = ReadScratch(temporary);
        var evidence = Path.Combine(lease.OperationDirectory(operationId), UnpromotedFileName);
        StagingWorkspace.AssertSafe(evidence);
        if (File.Exists(evidence))
        {
            if (!ReadScratch(evidence).AsSpan().SequenceEqual(pending))
                throw new ReleaseValidationException("Conflicting unpromoted journal evidence.");
            // Remove only a byte-identical duplicate; the retained non-authoritative copy remains.
            File.Delete(temporary);
        }
        else File.Move(temporary, evidence, overwrite: false);

        static byte[] ReadScratch(string path)
        {
            ManagedInstallLease.AssertPrivate(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > RecoveryRules.JournalLimit)
                throw new ReleaseValidationException("Unpromoted journal exceeds its bound.");
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            if (file.ReadByte() != -1) throw new ReleaseValidationException("Unpromoted journal changed.");
            return bytes;
        }
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
