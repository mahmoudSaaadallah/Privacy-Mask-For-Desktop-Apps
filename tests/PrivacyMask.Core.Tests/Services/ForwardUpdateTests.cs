using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ForwardUpdateTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ReauthenticatesPreparedCopiesWithoutNeedingDownloadStage(bool primary, bool secondary)
    {
        using var f = new PreparationFixture(primary, secondary, empty: true);
        using var client = f.Trust.Client();
        var prepared = await f.Preparer(client).PrepareAsync(f.Install.OperationId, f.Context);
        // Stage is not apply authority; retained signed target and exact baseline copies suffice.
        foreach (var file in Directory.GetFiles(f.Stage)) File.Delete(file);
        Directory.Delete(f.Stage, false);
        using (var verified = await Verify(f, client))
        {
            Assert.Equal(prepared, verified.Record);
            Assert.Equal(prepared.TargetVersion, verified.Manifest.Version);
            Assert.Equal(prepared.TargetApp, verified.Target.Snapshot);
            Assert.Throws<IOException>(() => File.WriteAllText(Path.Combine(f.Install.Operation, "target-app.exe"), "changed"));
            Assert.Throws<IOException>(() => File.WriteAllText(Path.Combine(f.Install.Operation, "original-app.exe"), "changed"));
            verified.AssertCurrentBaseline();
            verified.AssertFresh();
        }
        Assert.Equal(6, f.Trust.Handler.Requests.Count);
        f.AssertUntouched(noJournal: false);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("signature")]
    [InlineData("target")]
    [InlineData("original-app")]
    [InlineData("original-primary")]
    [InlineData("original-secondary")]
    [InlineData("current-app")]
    [InlineData("current-primary")]
    [InlineData("current-secondary")]
    [InlineData("operation-extra")]
    [InlineData("app-extra")]
    [InlineData("step")]
    [InlineData("journal-hash")]
    [InlineData("journal-target")]
    public async Task ChangedPreparedOrCurrentStateFailsWithoutMutation(string change)
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var record = await f.Preparer(client).PrepareAsync(f.Install.OperationId, f.Context);
        var name = change switch {
            "metadata" => "update-manifest.json", "signature" => "update-manifest.sig.json", "target" => "target-app.exe",
            "original-app" => "original-app.exe", "original-primary" => "original-settings.json", "original-secondary" => "original-settings.bak", _ => null };
        if (name is not null) File.AppendAllText(Path.Combine(f.Install.Operation, name), "tampered");
        if (change == "current-app") File.AppendAllText(f.Install.App, "changed");
        if (change == "current-primary") File.AppendAllText(f.Install.Primary, "changed");
        if (change == "current-secondary") File.AppendAllText(f.Install.Secondary, "changed");
        if (change == "operation-extra") PreparationFixture.Write(Path.Combine(f.Install.Operation, "forward-app.tmp"), [1]);
        if (change == "app-extra") PreparationFixture.Write(Path.Combine(Path.GetDirectoryName(f.Install.App)!, "foreign.txt"), [1]);
        if (change == "step") new RecoveryJournalStore(f.Install.Lease).Write(record with { Step = RecoveryStep.Handoff, Revision = 2 });
        if (change is "journal-hash" or "journal-target")
            File.WriteAllBytes(Path.Combine(f.Install.Root, "update-journal.json"), ContractJson.Write(change == "journal-hash"
                ? record with { ManifestSha256 = new string('e', 64) } : record with { TargetVersion = "2.0.0-beta.4" }));
        var app = File.ReadAllBytes(f.Install.App);
        var primary = File.ReadAllBytes(f.Install.Primary);
        var secondary = File.ReadAllBytes(f.Install.Secondary);
        var journal = File.ReadAllBytes(Path.Combine(f.Install.Root, "update-journal.json"));
        await Assert.ThrowsAnyAsync<Exception>(() => Verify(f, client));
        Assert.Equal(app, File.ReadAllBytes(f.Install.App));
        Assert.Equal(primary, File.ReadAllBytes(f.Install.Primary));
        Assert.Equal(secondary, File.ReadAllBytes(f.Install.Secondary));
        Assert.Equal(journal, File.ReadAllBytes(Path.Combine(f.Install.Root, "update-journal.json")));
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("deleted")]
    [InlineData("changed-release")]
    [InlineData("os")]
    [InlineData("schema")]
    [InlineData("channel")]
    [InlineData("installed-version")]
    [InlineData("operation-id")]
    public async Task RechecksFreshSelectionAndHostCompatibility(string change)
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        await f.Preparer(client).PrepareAsync(f.Install.OperationId, f.Context);
        if (change == "offline") f.Trust.Handler.Intercept = (_, _) => throw new HttpRequestException("private URL");
        if (change == "deleted") f.Trust.Handler.Releases.Clear();
        if (change == "changed-release") { f.Trust.Handler.Releases.Clear(); f.Trust.Handler.Releases.Add(f.Trust.Release("2.0.0-beta.4")); }
        var context = change switch {
            "os" => f.Context with { WindowsBuild = 99999 }, "schema" => f.Context with { SettingsSchema = 13 },
            "channel" => f.Context with { Channel = UpdateChannel.Stable }, "installed-version" => f.Context with { InstalledVersion = "1.0.0" }, _ => f.Context };
        await Assert.ThrowsAnyAsync<Exception>(() => ForwardUpdateInputs.VerifyAsync(f.Install.Lease,
            change == "operation-id" ? Guid.NewGuid().ToString("N") : f.Install.OperationId, context, f.Trust.Verifier, client, f.Trust.Clock,
            CancellationToken.None, (_, _, _) => { }));
        f.AssertUntouched(noJournal: false);
    }

    [Fact]
    public async Task RechecksCurrentSettingsAndFreshnessImmediatelyBeforeReplacement()
    {
        using var f = new PreparationFixture(primary: false);
        using var client = f.Trust.Client();
        await f.Preparer(client).PrepareAsync(f.Install.OperationId, f.Context);
        using var verified = await Verify(f, client);
        PreparationFixture.Write(f.Install.Primary, [1]);
        Assert.Throws<ReleaseValidationException>(verified.AssertCurrentBaseline);
        f.Trust.Clock.Now += TimeSpan.FromHours(25);
        Assert.Throws<ReleaseValidationException>(verified.AssertFresh);
        f.Trust.Clock.Now -= TimeSpan.FromHours(26);
        Assert.Throws<ReleaseValidationException>(verified.AssertFresh);
    }

    [Fact]
    public void ExitEvidenceCannotBeCreatedForAnUnrelatedLiveProcessOrWrongOperation()
    {
        using var f = new RecoveryFixture();
        using var self = Process.GetCurrentProcess();
        Assert.Throws<ReleaseValidationException>(() => AppProcessExit.Capture(f.Lease, f.OperationId, self.Id));
        Assert.Throws<ReleaseValidationException>(() => AppProcessExit.Capture(f.Lease, Guid.NewGuid().ToString("N"), self.Id));
        f.AssertOriginal();
    }

    private static Task<ForwardUpdateInputs> Verify(PreparationFixture f, GitHubReleaseClient client) =>
        ForwardUpdateInputs.VerifyAsync(f.Install.Lease, f.Install.OperationId, f.Context, f.Trust.Verifier, client, f.Trust.Clock,
            CancellationToken.None, (_, _, _) => { }); // Synthetic PE seam only, never process/exit/launch bypass.
}
