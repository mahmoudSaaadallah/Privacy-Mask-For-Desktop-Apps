using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class PreparedAbandonmentTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Abandonment_keeps_files_and_archives_before_writers_resume(bool primary, bool backup)
    {
        using var fixture = new RecoveryFixture(primary, backup);
        var store = new RecoveryJournalStore(fixture.Lease);
        store.AbandonPrepared(fixture.OperationId);
        Assert.Null(store.Read());
        fixture.AssertOriginal();
        Assert.True(File.Exists(Path.Combine(fixture.Operation, "terminal-journal.json")));
        // Future boot must not restore an abandoned old settings snapshot over newer edits.
        using (var output = ManagedInstallLease.CreatePrivateFile(Path.Combine(fixture.Root, "new-settings-proof"))) output.WriteByte(1);
        Assert.Equal(RecoveryOutcome.NoPendingOperation, new RecoveryRestorer(fixture.Lease).Recover().Outcome);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("settings")]
    [InlineData("retained")]
    [InlineData("operation")]
    [InlineData("armed")]
    public void Changed_or_armed_state_cannot_be_retired(string change)
    {
        using var fixture = new RecoveryFixture();
        if (change == "app") File.WriteAllText(fixture.App, "changed");
        if (change == "settings") File.WriteAllText(fixture.Primary, "changed");
        if (change == "retained") File.WriteAllText(Path.Combine(fixture.Operation, "original-app.exe"), "changed");
        if (change == "armed") fixture.Advance(RecoveryStep.Handoff);
        var store = new RecoveryJournalStore(fixture.Lease);
        Assert.Throws<ReleaseValidationException>(() => store.AbandonPrepared(
            change == "operation" ? Guid.NewGuid().ToString("N") : fixture.OperationId));
        Assert.NotNull(store.Read());
    }
}
