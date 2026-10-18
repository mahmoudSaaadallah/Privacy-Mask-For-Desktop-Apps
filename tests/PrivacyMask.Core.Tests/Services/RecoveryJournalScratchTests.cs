using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RecoveryJournalScratchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Repeated_unpromoted_writes_keep_one_root_scratch_and_one_evidence_copy(int point)
    {
        using var f = new RecoveryFixture();
        var next = f.Record with { Step = RecoveryStep.Handoff, Revision = 2 };
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var store = new RecoveryJournalStore(f.Lease, current => {
                if ((int)current == point) throw new SimulatedRecoveryCrash();
            });
            Assert.Throws<SimulatedRecoveryCrash>(() => store.Write(next));
            Assert.Equal(f.Record, store.Read());
            Assert.Single(Directory.GetFiles(f.Root, ".update-journal*.tmp"));
            Assert.Equal(attempt == 0 ? 0 : 1, Directory.GetFiles(f.Operation, RecoveryJournalStore.UnpromotedFileName).Length);
        }
        new RecoveryJournalStore(f.Lease).Write(next);
        Assert.Equal(next, new RecoveryJournalStore(f.Lease).Read());
        Assert.Empty(Directory.GetFiles(f.Root, ".update-journal*.tmp"));
    }

    [Fact]
    public void A_flushed_but_unpromoted_commit_does_not_prevent_rollback()
    {
        using var f = new RecoveryFixture();
        f.Advance(RecoveryStep.AwaitingReadiness);
        f.ReplaceFiles();
        var commit = f.Record with { Step = RecoveryStep.Committed, Revision = f.Record.Revision + 1 };
        Assert.Throws<SimulatedRecoveryCrash>(() => new RecoveryJournalStore(f.Lease, point => {
            if (point == JournalCheckpoint.TempFlushed) throw new SimulatedRecoveryCrash();
        }).Write(commit));
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        Assert.True(File.Exists(Path.Combine(f.Operation, RecoveryJournalStore.UnpromotedFileName)));
        f.AssertOriginal();
    }

    [Fact]
    public void Conflicting_journal_evidence_is_preserved_without_allocating_another_temp()
    {
        using var f = new RecoveryFixture();
        RetentionFixture.Write(Path.Combine(f.Root, RecoveryJournalStore.TemporaryFileName), [99]);
        RetentionFixture.Write(Path.Combine(f.Operation, RecoveryJournalStore.UnpromotedFileName), [7]);
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Throws<ReleaseValidationException>(() => new RecoveryJournalStore(f.Lease).Write(
                f.Record with { Step = RecoveryStep.Handoff, Revision = 2 }));
        Assert.Equal(f.Record, new RecoveryJournalStore(f.Lease).Read());
        Assert.Single(Directory.GetFiles(f.Root, ".update-journal*.tmp"));
        Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(Path.Combine(f.Root, RecoveryJournalStore.TemporaryFileName)));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(f.Operation, RecoveryJournalStore.UnpromotedFileName)));
    }
}
