using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RecoveryRestorerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UncommittedStatesRestoreMatchingAppAndBothSettings(int pendingState)
    {
        using var f = new RecoveryFixture();
        f.Advance((RecoveryStep)pendingState);
        f.ReplaceFiles();
        File.WriteAllText(Path.Combine(f.Root, "settings.v1.json.corrupt-evidence"), "preserve me");
        var result = new RecoveryRestorer(f.Lease).Recover();
        Assert.Equal(RecoveryOutcome.Recovered, result.Outcome);
        f.AssertOriginal();
        Assert.Equal(RecoveryStep.Recovered, new RecoveryJournalStore(f.Lease).Read()!.Step);
        Assert.Equal(f.NewApp, File.ReadAllBytes(Path.Combine(f.Operation, "failed-app")));
        Assert.True(File.Exists(Path.Combine(f.Root, "settings.v1.json.corrupt-evidence")));
        // Recovery is durable and repeatable; it does not consume the original backup.
        f.Reopen();
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        f.AssertOriginal();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void PreservesIndependentAbsenceAndEmptySettings(bool primary, bool secondary, bool empty)
    {
        using var f = new RecoveryFixture(primary, secondary, empty);
        f.ReplaceFiles();
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        f.AssertOriginal();
    }

    [Theory]
    [InlineData(0, "journal")]
    [InlineData(1, "app")]
    [InlineData(2, "app")]
    [InlineData(1, "primary")]
    [InlineData(2, "primary")]
    [InlineData(1, "secondary")]
    [InlineData(2, "secondary")]
    [InlineData(3, "journal")]
    public void RestartResumesEveryRestoreBoundary(int point, string label)
    {
        using var f = new RecoveryFixture();
        f.Advance(RecoveryStep.AwaitingReadiness);
        f.ReplaceFiles();
        var restorer = new RecoveryRestorer(f.Lease, (actual, file) =>
        {
            if ((int)actual == point && file == label) throw new SimulatedRecoveryCrash();
        });
        Assert.Throws<SimulatedRecoveryCrash>(() => restorer.Recover());
        f.Reopen(); // New host/lease; no in-memory recovery continuation.
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        f.AssertOriginal();
    }

    [Theory]
    [InlineData("original-app.exe")]
    [InlineData("original-settings.json")]
    [InlineData("original-settings.bak")]
    public void AllBackupsMustAuthenticateBeforeAnyInstalledFileChanges(string corruptedBackup)
    {
        using var f = new RecoveryFixture();
        f.ReplaceFiles();
        File.WriteAllText(Path.Combine(f.Operation, corruptedBackup), "corrupt");
        var result = new RecoveryRestorer(f.Lease).Recover();
        Assert.Equal(RecoveryOutcome.RepairRequired, result.Outcome);
        Assert.Equal("recovery-backup-missing-or-changed", result.Reason);
        Assert.Equal(f.NewApp, File.ReadAllBytes(f.App));
        Assert.Equal("new schema primary", File.ReadAllText(f.Primary));
        Assert.Equal(RecoveryStep.Prepared, new RecoveryJournalStore(f.Lease).Read()!.Step);
    }

    [Fact]
    public void MissingAppIsRestoredButUnknownPayloadOrExtraAppFilesArePreserved()
    {
        using (var f = new RecoveryFixture())
        {
            File.Delete(f.App);
            Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
            f.AssertOriginal();
        }
        using (var f = new RecoveryFixture())
        {
            File.WriteAllText(f.App, "unrecognized exe bytes");
            Assert.Equal("unknown-installed-payload", new RecoveryRestorer(f.Lease).Recover().Reason);
            Assert.Equal("unrecognized exe bytes", File.ReadAllText(f.App));
        }
        using (var f = new RecoveryFixture())
        {
            f.ReplaceFiles();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.App)!, "foreign.dll"), "preserve");
            Assert.Equal("unexpected-app-directory-content", new RecoveryRestorer(f.Lease).Recover().Reason);
            Assert.Equal(f.NewApp, File.ReadAllBytes(f.App));
        }
    }

    [Fact]
    public void CommitIsNeverRolledBackAndTerminalRecoveryDoesNotResetLaterSettings()
    {
        using (var f = new RecoveryFixture())
        {
            f.Advance(RecoveryStep.Committed);
            f.ReplaceFiles();
            File.Delete(Path.Combine(f.Operation, "original-app.exe"));
            Assert.Equal(RecoveryOutcome.Committed, new RecoveryRestorer(f.Lease).Recover().Outcome);
            Assert.Equal("new schema primary", File.ReadAllText(f.Primary));
            File.Delete(f.App);
            Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
            Assert.False(File.Exists(f.App)); // No guessed rollback of a committed installation.
        }
        using (var f = new RecoveryFixture())
        {
            f.ReplaceFiles();
            Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
            File.WriteAllText(f.Primary, "user edits after the old app restarted");
            f.Reopen();
            Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
            Assert.Equal("user edits after the old app restarted", File.ReadAllText(f.Primary));
        }
    }

    [Theory]
    [InlineData("app")]
    [InlineData("primary")]
    [InlineData("secondary")]
    public void LockedFileLeavesBackupAndRollingBackIntentForRetry(string label)
    {
        using var f = new RecoveryFixture();
        f.ReplaceFiles();
        // A handle permitting reads but denying delete simulates a locked move without a running app.
        var lockedPath = label switch { "primary" => f.Primary, "secondary" => f.Secondary, _ => f.App };
        using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
            Assert.Equal(label == "app" ? f.NewApp : f.OldApp, File.ReadAllBytes(f.App));
            Assert.Equal(RecoveryStep.RollingBack, new RecoveryJournalStore(f.Lease).Read()!.Step);
            Assert.Equal(f.OldApp, File.ReadAllBytes(Path.Combine(f.Operation, "original-app.exe")));
        }
        f.Reopen();
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        f.AssertOriginal();
    }

    [Fact]
    public void ForeignEvidenceAndMissingJournalNeverAuthorizeReplacement()
    {
        using var f = new RecoveryFixture();
        f.ReplaceFiles();
        var path = Path.Combine(f.Operation, "failed-primary");
        File.WriteAllText(path, "prior evidence");
        Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
        Assert.Equal("prior evidence", File.ReadAllText(path));
        Assert.Equal(f.NewApp, File.ReadAllBytes(f.App));
        File.Delete(Path.Combine(f.Root, "update-journal.json"));
        Assert.Equal(RecoveryOutcome.NoPendingOperation, new RecoveryRestorer(f.Lease).Recover().Outcome);
        Assert.Equal(f.NewApp, File.ReadAllBytes(f.App));
    }
}
