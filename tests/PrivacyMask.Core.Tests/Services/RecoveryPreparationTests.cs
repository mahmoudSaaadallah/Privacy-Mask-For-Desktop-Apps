using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RecoveryPreparationTests
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task PreparedRecordOwnsExactBaselinesAndSupportsKernelRecovery(bool primary, bool secondary, bool empty)
    {
        using var f = new PreparationFixture(primary, secondary, empty);
        using var client = f.Trust.Client();
        var inspections = new List<(string Version, string? Commit)>();
        var record = await f.Preparer(client, inspect: (_, version, commit) => inspections.Add((version, commit)))
            .PrepareAsync(f.Install.OperationId, f.Context);
        Assert.Equal(RecoveryStep.Prepared, record.Step);
        Assert.Equal(record, new RecoveryJournalStore(f.Install.Lease).Read());
        Assert.Equal(RecoveryFixture.Snapshot(f.Install.OldApp), record.OriginalApp);
        Assert.Equal(RecoveryFixture.Snapshot(f.Install.NewApp), record.TargetApp);
        Assert.Equal(RecoveryFixture.Snapshot(f.Install.OldPrimary), record.SettingsPrimary);
        Assert.Equal(RecoveryFixture.Snapshot(f.Install.OldSecondary), record.SettingsBackup);
        Assert.Equal(PreparationFixture.Hash(f.Manifest), record.ManifestSha256);
        Assert.Equal(PreparationFixture.Hash(f.Signature), record.SignatureSha256);
        Assert.Equal("2.0.0-beta.2", record.OriginalVersion);
        Assert.Equal(2, inspections.Count);
        Assert.Equal("2.0.0-beta.3", inspections[0].Version);
        Assert.Equal(new string('1', 40), inspections[0].Commit);
        Assert.Equal(f.Context.InstalledVersion, inspections[1].Version);
        Assert.Null(inspections[1].Commit);
        Assert.Equal(f.Manifest, File.ReadAllBytes(Path.Combine(f.Install.Operation, "update-manifest.json")));
        Assert.Equal(f.Signature, File.ReadAllBytes(Path.Combine(f.Install.Operation, "update-manifest.sig.json")));
        Assert.Equal(f.Install.NewApp, File.ReadAllBytes(Path.Combine(f.Install.Operation, "target-app.exe")));
        Assert.All(Directory.GetFiles(f.Install.Operation), ManagedInstallLease.AssertPrivate);
        f.AssertUntouched(noJournal: false);
        Assert.Equal(3, f.Trust.Handler.Requests.Count); // list + signed metadata, never another ZIP
        Assert.All(f.Trust.Handler.Requests, request => Assert.False(request.HasCredentials));
        var text = File.ReadAllText(Path.Combine(f.Install.Root, "update-journal.json"));
        Assert.DoesNotContain("testVerifier", text);
        Assert.DoesNotContain(f.Install.Root, text);

        // Logical interrupted apply: the retained baseline is usable by the existing rollback kernel.
        File.WriteAllBytes(f.Install.App, f.Install.NewApp);
        WriteReplacement(f.Install.Primary, "replacement migration primary");
        WriteReplacement(f.Install.Secondary, "replacement migration backup");
        f.Install.Reopen();
        Assert.Equal(new RecoveryResult(RecoveryOutcome.Recovered), new RecoveryRestorer(f.Install.Lease).Recover());
        f.Install.AssertOriginal();

        static void WriteReplacement(string path, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (File.Exists(path)) File.WriteAllBytes(path, bytes);
            else PreparationFixture.Write(path, bytes); // Explicit SID owner even on elevated CI workers.
            ManagedInstallLease.AssertPrivate(path);
        }
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("signature")]
    [InlineData("archive")]
    [InlineData("payload")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("directory")]
    [InlineData("public-acl")]
    [InlineData("oversized")]
    public async Task CorruptStagingNeverReachesDiscoveryOrCreatesBaselines(string corruption)
    {
        using var f = new PreparationFixture();
        var path = Path.Combine(f.Stage, corruption switch {
            "manifest" => "update-manifest.json", "signature" => "update-manifest.sig.json", "archive" => "package.zip", _ => "PrivacyMask.App.exe" });
        switch (corruption)
        {
            case "missing": File.Delete(path); break;
            case "extra": PreparationFixture.Write(Path.Combine(f.Stage, "unexpected.txt"), [1]); break;
            case "directory": File.Delete(path); Directory.CreateDirectory(path); break;
            case "public-acl":
                var security = new FileInfo(path).GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
                new FileInfo(path).SetAccessControl(security);
                break;
            case "oversized": using (var stream = File.OpenWrite(path)) stream.SetLength(ManifestRules.MaximumFileSize + 1); break;
            default: File.AppendAllText(path, "tampered"); break;
        }
        using var client = f.Trust.Client();
        await Assert.ThrowsAsync<UpdatePreparationException>(() => f.Preparer(client).PrepareAsync(f.Install.OperationId, f.Context));
        Assert.Empty(f.Trust.Handler.Requests);
        Assert.False(Directory.Exists(f.Install.Operation));
        f.AssertUntouched();
    }

    [Fact]
    public async Task ProductionInspectorRejectsSyntheticExeInsteadOfTrustingFilenameOrHash()
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var preparer = new RecoveryPreparer(f.Install.Lease, f.Trust.Verifier, client);
        var error = await Assert.ThrowsAsync<UpdatePreparationException>(() => preparer.PrepareAsync(f.Install.OperationId, f.Context));
        Assert.Equal("preparation-validation-failed", error.Reason);
        Assert.Empty(f.Trust.Handler.Requests);
        f.AssertUntouched();
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("deleted")]
    [InlineData("draft")]
    [InlineData("rate-limit")]
    [InlineData("changed-signed-selection")]
    [InlineData("clock-backwards")]
    [InlineData("expired")]
    public async Task RequiresMatchingFreshPublishedReleaseWithoutTimestampBypass(string failure)
    {
        using var f = new PreparationFixture();
        if (failure == "deleted") f.Trust.Handler.Releases.Clear();
        if (failure == "draft") f.Trust.Handler.Releases[0]!["draft"] = true;
        if (failure == "changed-signed-selection")
        {
            f.Trust.Handler.Releases.Clear();
            f.Trust.Handler.Releases.Add(f.Trust.Release("2.0.0-beta.4"));
        }
        if (failure == "offline") f.Trust.Handler.Intercept = (_, _) => throw new HttpRequestException("private URL and token");
        if (failure == "rate-limit") f.Trust.Handler.Intercept = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = f.Trust.Client();
        var preparer = f.Preparer(client, checkpoint: (point, _) => {
            if (point == PreparationCheckpoint.FreshReleaseChecked)
                f.Trust.Clock.Now += failure == "clock-backwards" ? TimeSpan.FromSeconds(-1) : failure == "expired" ? TimeSpan.FromHours(25) : TimeSpan.Zero;
        });
        var error = await Assert.ThrowsAsync<UpdatePreparationException>(() => preparer.PrepareAsync(f.Install.OperationId, f.Context));
        Assert.Equal("fresh-release-check-required", error.Reason);
        Assert.DoesNotContain("private", error.ToString());
        Assert.False(Directory.Exists(f.Install.Operation));
        f.AssertUntouched();
    }

    [Theory]
    [InlineData("portable")]
    [InlineData("os")]
    [InlineData("schema")]
    [InlineData("protocol")]
    [InlineData("runtime")]
    [InlineData("channel")]
    [InlineData("older")]
    [InlineData("equal")]
    [InlineData("operation")]
    public async Task RechecksCompatibilityAndOperationIdentityBeforeDiscovery(string change)
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var context = change switch {
            "portable" => f.Context with { Installation = InstallationKind.Portable },
            "os" => f.Context with { WindowsBuild = 99999 },
            "schema" => f.Context with { SettingsSchema = 13 },
            "protocol" => f.Context with { UpdaterProtocol = 2 },
            "runtime" => f.Context with { Runtime = "win-arm64" },
            "channel" => f.Context with { Channel = UpdateChannel.Stable },
            "older" => f.Context with { InstalledVersion = "3.0.0" },
            "equal" => f.Context with { InstalledVersion = "2.0.0-beta.3+another-build" }, _ => f.Context };
        await Assert.ThrowsAsync<UpdatePreparationException>(() => f.Preparer(client).PrepareAsync(change == "operation" ? "../outside" : f.Install.OperationId, context));
        Assert.Empty(f.Trust.Handler.Requests);
        f.AssertUntouched();
    }

    [Fact]
    public async Task BudgetIncludesBothSettingsFilesRollbackScratchAndWorstCaseGrowth()
    {
        var app = new RecoveryFile(true, 100, new string('a', 64));
        var primary = app with { Size = 7 };
        var secondary = app with { Size = 11 };
        var target = app with { Size = 200 };
        var fixedReserve = 2 * RecoveryRules.SettingsLimit + PreparationBudget.Margin + PreparationBudget.MetadataReserve;
        Assert.Equal(636 + fixedReserve, PreparationBudget.Required(app, primary, secondary, target));
        Assert.Equal(318 + fixedReserve, PreparationBudget.Required(app, primary, secondary, target, true));
        Assert.Throws<OverflowException>(() => PreparationBudget.Required(app with { Size = long.MaxValue }, primary, secondary, target));
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var error = await Assert.ThrowsAsync<UpdatePreparationException>(() => f.Preparer(client, free: _ => 0).PrepareAsync(f.Install.OperationId, f.Context));
        Assert.Equal("insufficient-recovery-space", error.Reason);
        Assert.False(Directory.Exists(f.Install.Operation));
        f.AssertUntouched();
    }

    [Fact]
    public async Task FallingFreeSpaceLeavesEvidenceButNoPreparedOrInstalledMutation()
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var calls = 0;
        var error = await Assert.ThrowsAsync<UpdatePreparationException>(() => f.Preparer(client, free: _ => ++calls == 1 ? long.MaxValue : 0)
            .PrepareAsync(f.Install.OperationId, f.Context));
        Assert.Equal("insufficient-recovery-space", error.Reason);
        Assert.Equal(2, calls);
        Assert.True(File.Exists(Path.Combine(f.Install.Operation, "original-app.exe")));
        f.AssertUntouched();
    }

    [Theory]
    [InlineData(PreparationCheckpoint.DirectoryCreated, "operation")]
    [InlineData(PreparationCheckpoint.FileCopied, "original-app.exe")]
    [InlineData(PreparationCheckpoint.FileCopied, "original-settings.json")]
    [InlineData(PreparationCheckpoint.FileCopied, "original-settings.bak")]
    [InlineData(PreparationCheckpoint.FileCopied, "target-app.exe")]
    [InlineData(PreparationCheckpoint.FileCopied, "update-manifest.json")]
    [InlineData(PreparationCheckpoint.FileCopied, "update-manifest.sig.json")]
    [InlineData(PreparationCheckpoint.BeforeJournal, "journal")]
    public async Task InterruptionBeforePromotionNeverAuthorizesAnOperation(object checkpoint, string label)
    {
        var point = (PreparationCheckpoint)checkpoint;
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var preparer = f.Preparer(client, checkpoint: (actual, file) => { if (actual == point && file == label) throw new SimulatedRecoveryCrash(); });
        await Assert.ThrowsAsync<SimulatedRecoveryCrash>(() => preparer.PrepareAsync(f.Install.OperationId, f.Context));
        f.Install.Reopen();
        Assert.Equal(RecoveryOutcome.NoPendingOperation, new RecoveryRestorer(f.Install.Lease).Recover().Outcome);
        f.AssertUntouched();
        // Evidence collision is not permission to overwrite/reuse a partially prepared operation.
        await Assert.ThrowsAsync<UpdatePreparationException>(() => f.Preparer(client).PrepareAsync(f.Install.OperationId, f.Context));
        f.AssertUntouched();
    }

    [Fact]
    public async Task CrashAfterPromotionLeavesAuthoritativeRecoverablePreparedRecord()
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        await Assert.ThrowsAsync<SimulatedRecoveryCrash>(() => f.Preparer(client, checkpoint: (point, _) => {
            if (point == PreparationCheckpoint.Prepared) throw new SimulatedRecoveryCrash();
        }).PrepareAsync(f.Install.OperationId, f.Context));
        f.Install.Reopen();
        Assert.Equal(RecoveryStep.Prepared, new RecoveryJournalStore(f.Install.Lease).Read()!.Step);
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Install.Lease).Recover().Outcome);
        f.Install.AssertOriginal();
    }

    [Fact]
    public async Task PinsAllPresentInputsAndDetectsCreationOfPreviouslyAbsentSettings()
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        await f.Preparer(client, checkpoint: (point, _) => {
            if (point == PreparationCheckpoint.Authenticated)
                foreach (var path in Directory.GetFiles(f.Stage)) Assert.Throws<IOException>(() => File.WriteAllText(path, "changed"));
            if (point == PreparationCheckpoint.BeforeJournal)
                foreach (var path in new[] { f.Install.App, f.Install.Primary, f.Install.Secondary })
                {
                    Assert.Throws<IOException>(() => File.WriteAllText(path, "changed"));
                    Assert.Throws<IOException>(() => File.Delete(path));
                }
        }).PrepareAsync(f.Install.OperationId, f.Context);
        f.AssertUntouched(noJournal: false);

        using var absent = new PreparationFixture(primary: false);
        using var otherClient = absent.Trust.Client();
        await Assert.ThrowsAsync<UpdatePreparationException>(() => absent.Preparer(otherClient, checkpoint: (point, _) => {
            if (point == PreparationCheckpoint.BeforeJournal) PreparationFixture.Write(absent.Install.Primary, [1]);
        }).PrepareAsync(absent.Install.OperationId, absent.Context));
        Assert.Null(new RecoveryJournalStore(absent.Install.Lease).Read());
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(absent.Install.Primary)); // Do not undo another writer.
    }

    [Theory]
    [InlineData("original-app.exe")]
    [InlineData("original-settings.json")]
    [InlineData("original-settings.bak")]
    [InlineData("target-app.exe")]
    [InlineData("update-manifest.json")]
    [InlineData("update-manifest.sig.json")]
    public async Task RechecksEveryRetainedCopyBeforePublishingPrepared(string file)
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        await Assert.ThrowsAsync<UpdatePreparationException>(() => f.Preparer(client, checkpoint: (point, _) => {
            if (point == PreparationCheckpoint.BeforeJournal) File.AppendAllText(Path.Combine(f.Install.Operation, file), "corrupt");
        }).PrepareAsync(f.Install.OperationId, f.Context));
        f.AssertUntouched();
    }

    [Fact]
    public async Task CancellationBeforePromotionAndLateCancellationHaveDistinctDurableOutcomes()
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Preparer(client).PrepareAsync(f.Install.OperationId, f.Context, canceled.Token));
        Assert.Empty(f.Trust.Handler.Requests);
        using var before = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Preparer(client, checkpoint: (point, _) => {
            if (point == PreparationCheckpoint.BeforeJournal) before.Cancel();
        }).PrepareAsync(f.Install.OperationId, f.Context, before.Token));
        f.AssertUntouched();
        using var next = new PreparationFixture();
        using var nextClient = next.Trust.Client();
        using var late = new CancellationTokenSource();
        var record = await next.Preparer(nextClient, checkpoint: (point, _) => {
            if (point == PreparationCheckpoint.Prepared) late.Cancel();
        }).PrepareAsync(next.Install.OperationId, next.Context, late.Token);
        Assert.Equal(record, new RecoveryJournalStore(next.Install.Lease).Read());
    }

    [Fact]
    public async Task SingleFlightAndLeasePreventCompetingPreparationAndReleaseOnCancellation()
    {
        using var f = new PreparationFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Trust.Handler.Intercept = async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); };
        using var client = f.Trust.Client();
        using var cancel = new CancellationTokenSource();
        var preparer = f.Preparer(client);
        var pending = preparer.PrepareAsync(f.Install.OperationId, f.Context, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("preparation-in-progress", (await Assert.ThrowsAsync<UpdatePreparationException>(() => preparer.PrepareAsync(f.Install.OperationId, f.Context))).Reason);
        Assert.Throws<IOException>(() => new ManagedInstallLease(f.Install.Root, f.Install.InstallId));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        f.Trust.Handler.Intercept = null;
        Assert.Equal(RecoveryStep.Prepared, (await preparer.PrepareAsync(f.Install.OperationId, f.Context)).Step);
        Assert.Equal("existing-recovery-operation", (await Assert.ThrowsAsync<UpdatePreparationException>(() => preparer.PrepareAsync(Guid.NewGuid().ToString("N"), f.Context))).Reason);
    }

    [Theory]
    [InlineData("locked-app")]
    [InlineData("locked-primary")]
    [InlineData("locked-secondary")]
    [InlineData("huge-primary")]
    [InlineData("huge-secondary")]
    [InlineData("extra-app-file")]
    [InlineData("old-identity")]
    [InlineData("copy-io-failure")]
    public async Task InvalidBaselineOrIoFailureNeverWritesInstalledFilesOrPublishesPrepared(string failure)
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var path = failure.EndsWith("primary", StringComparison.Ordinal) ? f.Install.Primary
            : failure.EndsWith("secondary", StringComparison.Ordinal) ? f.Install.Secondary : f.Install.App;
        if (failure.StartsWith("huge", StringComparison.Ordinal))
            using (var stream = File.OpenWrite(path)) stream.SetLength(RecoveryRules.SettingsLimit + 1);
        if (failure == "extra-app-file") PreparationFixture.Write(Path.Combine(Path.GetDirectoryName(f.Install.App)!, "foreign.txt"), [1]);
        var original = RecoveryRestorer.Inspect(path, ManifestRules.MaximumFileSize);
        using (var locked = failure.StartsWith("locked", StringComparison.Ordinal) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            var error = await Assert.ThrowsAsync<UpdatePreparationException>(() => f.Preparer(client,
                inspect: (_, _, commit) => { if (failure == "old-identity" && commit is null) throw new ReleaseValidationException("private path"); },
                checkpoint: (point, _) => { if (failure == "copy-io-failure" && point == PreparationCheckpoint.DirectoryCreated) throw new IOException("private path and token"); })
                .PrepareAsync(f.Install.OperationId, f.Context));
            Assert.DoesNotContain("private path", error.ToString());
        }
        Assert.Equal(original, RecoveryRestorer.Inspect(path, ManifestRules.MaximumFileSize));
        Assert.Null(new RecoveryJournalStore(f.Install.Lease).Read());
        Assert.False(File.Exists(Path.Combine(f.Install.Operation, "original-app.exe")));
    }

    [Fact]
    public async Task RechecksFreshnessAfterCopiesAndDoesNotCaptureUiContext()
    {
        using var f = new PreparationFixture();
        using var client = f.Trust.Client();
        var ui = new SynchronizationContext();
        var previous = SynchronizationContext.Current;
        var observed = new List<SynchronizationContext?>();
        Task<RecoveryJournal> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(ui);
            pending = f.Preparer(client, checkpoint: (point, _) => {
                observed.Add(SynchronizationContext.Current);
                if (point == PreparationCheckpoint.BeforeJournal) f.Trust.Clock.Now += TimeSpan.FromHours(25);
            }).PrepareAsync(f.Install.OperationId, f.Context);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        var error = await Assert.ThrowsAsync<UpdatePreparationException>(() => pending);
        Assert.Equal("fresh-release-check-required", error.Reason);
        Assert.NotEmpty(observed);
        Assert.All(observed, context => Assert.NotSame(ui, context));
        f.AssertUntouched();
    }
}
