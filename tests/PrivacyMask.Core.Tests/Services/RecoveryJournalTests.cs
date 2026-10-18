using System.IO;
using System.Text;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RecoveryJournalTests
{
    [Theory]
    [InlineData("operation")]
    [InlineData("install")]
    [InlineData("revision")]
    [InlineData("step")]
    [InlineData("version")]
    [InlineData("file")]
    [InlineData("absence")]
    [InlineData("settings-size")]
    [InlineData("manifest")]
    [InlineData("schema")]
    public void RejectsInvalidRecordFields(string field)
    {
        using var f = new RecoveryFixture();
        var invalid = field switch
        {
            "operation" => f.Record with { OperationId = "../outside" },
            "install" => f.Record with { InstallId = Guid.Empty.ToString("N") },
            "revision" => f.Record with { Revision = 0 },
            "step" => f.Record with { Step = (RecoveryStep)99 },
            "version" => f.Record with { TargetVersion = f.Record.OriginalVersion },
            "file" => f.Record with { OriginalApp = new(false, 0, null) },
            "absence" => f.Record with { SettingsPrimary = new(false, 0, new string('a', 64)) },
            "settings-size" => f.Record with { SettingsBackup = new(true, RecoveryRules.SettingsLimit + 1, new string('a', 64)) },
            "manifest" => f.Record with { ManifestSha256 = "untrusted" },
            _ => f.Record with { SchemaVersion = 2 }
        };
        Assert.Throws<ReleaseValidationException>(() => RecoveryRules.Validate(invalid));
    }

    [Fact]
    public void LeaseSerializesAccessAndRejectsMarkerChangesOrDisposedUse()
    {
        using var f = new RecoveryFixture();
        Assert.Throws<IOException>(() => new ManagedInstallLease(f.Root, f.InstallId));
        f.Reopen();
        f.Lease.AssertHeld();
        File.WriteAllBytes(Path.Combine(f.Root, "managed-install.json"), ContractJson.Write(new ManagedInstallMarker(1, ManifestRules.AppId, Guid.NewGuid().ToString("N"), 1)));
        Assert.Throws<ReleaseValidationException>(() => f.Lease.AssertHeld());
        Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
        f.Lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => new RecoveryJournalStore(f.Lease).Read());
    }

    [Fact]
    public void JournalDoesNotContainSettingsAndCannotChangeIdentityOrSkipTransitions()
    {
        using var f = new RecoveryFixture();
        var store = new RecoveryJournalStore(f.Lease);
        Assert.Equal(f.Record, store.Read());
        var text = File.ReadAllText(Path.Combine(f.Root, "update-journal.json"));
        Assert.DoesNotContain("never-put-this-marker", text);
        Assert.DoesNotContain(f.Root, text);
        Assert.Throws<ReleaseValidationException>(() => store.Write(f.Record with { Step = RecoveryStep.Committed, Revision = 2 }));
        Assert.Throws<ReleaseValidationException>(() => store.Write(f.Record with { Step = RecoveryStep.Handoff, Revision = 3 }));
        Assert.Throws<ReleaseValidationException>(() => store.Write(f.Record with { Step = RecoveryStep.Handoff, Revision = 2, TargetVersion = "3.0.0" }));
        Assert.Equal(f.Record, store.Read());
        f.AssertOriginal();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void OnlyPromotedJournalIsAuthoritativeAfterCrash(int checkpoint, bool promoted)
    {
        using var f = new RecoveryFixture();
        var store = new RecoveryJournalStore(f.Lease, point => { if ((int)point == checkpoint) throw new SimulatedRecoveryCrash(); });
        var next = f.Record with { Step = RecoveryStep.Handoff, Revision = 2 };
        Assert.Throws<SimulatedRecoveryCrash>(() => store.Write(next));
        f.Reopen();
        Assert.Equal(promoted ? next : f.Record, new RecoveryJournalStore(f.Lease).Read());
        Assert.Equal(!promoted, File.Exists(Path.Combine(f.Root, RecoveryJournalStore.TemporaryFileName)));
        f.AssertOriginal();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("truncated")]
    [InlineData("huge")]
    [InlineData("invalid-utf8")]
    [InlineData("other-install")]
    public void CorruptJournalNeverFallsBackOrTouchesInstalledFiles(string corruption)
    {
        using var f = new RecoveryFixture();
        var original = ContractJson.Write(f.Record);
        var text = Encoding.UTF8.GetString(original);
        byte[] bytes = corruption switch
        {
            "duplicate" => Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")),
            "unknown" => Encoding.UTF8.GetBytes(text[..^1] + ",\"path\":\"C:/outside\"}"),
            "truncated" => original[..^10],
            "huge" => new byte[RecoveryRules.JournalLimit + 1],
            "invalid-utf8" => [0xff],
            _ => ContractJson.Write(f.Record with { InstallId = Guid.NewGuid().ToString("N") })
        };
        var path = Path.Combine(f.Root, "update-journal.json");
        File.WriteAllBytes(path, bytes);
        Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        f.AssertOriginal();
    }
}
