using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RecoveryLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArchivesOnlyConsistentTerminalRecordAndPreservesAllEvidence(bool committed)
    {
        using var f = new RecoveryFixture();
        if (committed) { f.Advance(RecoveryStep.Committed); f.ReplaceFiles(); }
        else Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        var store = new RecoveryJournalStore(f.Lease);
        var record = store.Read();
        var active = Path.Combine(f.Root, "update-journal.json");
        var bytes = File.ReadAllBytes(active);
        var primary = File.ReadAllBytes(f.Primary);
        store.ArchiveTerminal(f.OperationId);
        Assert.Null(store.Read());
        var archive = Path.Combine(f.Operation, "terminal-journal.json");
        Assert.Equal(bytes, File.ReadAllBytes(archive));
        ManagedInstallLease.AssertPrivate(archive);
        Assert.Equal(f.OldApp, File.ReadAllBytes(Path.Combine(f.Operation, "original-app.exe")));
        Assert.Equal(primary, File.ReadAllBytes(f.Primary));
        Assert.Equal(RecoveryOutcome.NoPendingOperation, new RecoveryRestorer(f.Lease).Recover().Outcome);

        var nextId = Guid.NewGuid().ToString("N");
        ManagedInstallLease.CreatePrivateDirectory(Path.Combine(f.Root, "update-operations", "operation-" + nextId));
        store.Write(record! with { OperationId = nextId, Revision = 1, Step = RecoveryStep.Prepared });
        Assert.Equal(nextId, store.Read()!.OperationId);
        Assert.Equal(bytes, File.ReadAllBytes(archive));
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("handoff")]
    [InlineData("replacing")]
    [InlineData("awaiting")]
    [InlineData("wrong-id")]
    [InlineData("changed-app")]
    [InlineData("missing-baseline")]
    [InlineData("changed-baseline")]
    [InlineData("archive-collision")]
    [InlineData("archive-directory")]
    [InlineData("locked-journal")]
    public void InvalidArchivalNeverDropsActiveRecordOrOverwritesHistory(string failure)
    {
        using var f = new RecoveryFixture();
        var step = failure switch {
            "prepared" => RecoveryStep.Prepared, "handoff" => RecoveryStep.Handoff,
            "replacing" => RecoveryStep.Replacing, "awaiting" => RecoveryStep.AwaitingReadiness, _ => RecoveryStep.Committed };
        f.Advance(step);
        if (step == RecoveryStep.Committed) f.ReplaceFiles();
        if (failure == "changed-app") File.AppendAllText(f.App, "foreign");
        if (failure == "missing-baseline") File.Delete(Path.Combine(f.Operation, "original-app.exe"));
        if (failure == "changed-baseline") File.AppendAllText(Path.Combine(f.Operation, "original-settings.json"), "changed");
        var archive = Path.Combine(f.Operation, "terminal-journal.json");
        if (failure == "archive-collision") PreparationFixture.Write(archive, [1, 2, 3]);
        if (failure == "archive-directory") Directory.CreateDirectory(archive);
        var active = Path.Combine(f.Root, "update-journal.json");
        var bytes = File.ReadAllBytes(active);
        using (var locked = failure == "locked-journal" ? new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.Read) : null)
            Assert.ThrowsAny<Exception>(() => new RecoveryJournalStore(f.Lease).ArchiveTerminal(failure == "wrong-id" ? Guid.NewGuid().ToString("N") : f.OperationId));
        Assert.Equal(bytes, File.ReadAllBytes(active));
        if (failure == "archive-collision") Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(archive));
    }

    [Fact]
    public void CrashAfterTerminalRenamePreservesHistoryWithoutReactivatingRollback()
    {
        using var f = new RecoveryFixture();
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        var record = new RecoveryJournalStore(f.Lease).Read();
        Assert.Throws<SimulatedRecoveryCrash>(() => new RecoveryJournalStore(f.Lease, point => {
            if (point == JournalCheckpoint.TerminalArchived) throw new SimulatedRecoveryCrash();
        }).ArchiveTerminal(f.OperationId));
        f.Reopen();
        Assert.Null(new RecoveryJournalStore(f.Lease).Read());
        Assert.Equal(record, ContractJson.Read<RecoveryJournal>(File.ReadAllBytes(Path.Combine(f.Operation, "terminal-journal.json")), RecoveryRules.JournalLimit));
        f.AssertOriginal();
    }
}
