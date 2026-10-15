using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace PrivacyMask.Updates;

internal enum TransactionOutcome { Rejected, Committed, CommittedReceiptLost, RecoveredFiles, RepairRequired }
internal sealed record TransactionResult(TransactionOutcome Outcome, string? Reason = null);
internal enum TransactionCheckpoint { Validated, ScratchFlushed, HandoffSaved, ReplacingSaved, PayloadReplaced, AwaitingReadinessSaved, ChildStarted, ReadyReceived, CommittedSaved }

// Fault/measurement hooks are internal fixture seams, never serialized or exposed as CLI options.
internal sealed record TransactionTestHooks(Action<TransactionCheckpoint>? Checkpoint = null,
    Action<Process>? ChildStarted = null, Action<RecoveryStep, JournalCheckpoint>? Journal = null,
    TimeSpan? ReadyTimeout = null);

/// <summary>
/// Internal worker, not a production entrypoint. Requires frozen writers/consent/Panic gates in the
/// caller and retained exact-process exit evidence. Never restarts the recovered old app or changes entrypoints.
/// </summary>
internal sealed class UpdateTransaction
{
    private readonly ManagedInstallLease _lease;
    private readonly UpdateManifestVerifier _verifier;
    private readonly GitHubReleaseClient _discovery;
    private readonly TimeProvider _time;
    private readonly Func<string, long>? _availableSpace;
    private readonly TransactionTestHooks? _hooks;
    private int _busy;

    internal UpdateTransaction(ManagedInstallLease lease, UpdateManifestVerifier verifier, GitHubReleaseClient discovery)
        : this(lease, verifier, discovery, TimeProvider.System) { }

    internal UpdateTransaction(ManagedInstallLease lease, UpdateManifestVerifier verifier, GitHubReleaseClient discovery,
        TimeProvider time, Func<string, long>? availableSpace = null, TransactionTestHooks? hooks = null)
    { _lease = lease; _verifier = verifier; _discovery = discovery; _time = time; _availableSpace = availableSpace; _hooks = hooks; }

    internal async Task<TransactionResult> ApplyAsync(string operationId, UpdateCheckContext context,
        AppProcessExit oldProcess, bool startMinimized, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return new(TransactionOutcome.Rejected, "transaction-in-progress");
        try { return await ApplyCoreAsync(operationId, context, oldProcess, startMinimized, cancellationToken).ConfigureAwait(false); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private async Task<TransactionResult> ApplyCoreAsync(string operationId, UpdateCheckContext context,
        AppProcessExit oldProcess, bool startMinimized, CancellationToken cancellationToken)
    {
        var armed = false;
        var committed = false;
        var stopped = true;
        var reason = "transaction-failed";
        Process? child = null;
        ReadinessServer? server = null;
        Task? exchange = null;
        Task? exitObservation = null;
        ForwardUpdateInputs? inputs = null;
        CancellationTokenSource? readinessDeadline = null;
        using var preflightDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        preflightDeadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            preflightDeadline.Token.ThrowIfCancellationRequested();
            _lease.AssertHeld();
            var initial = new RecoveryJournalStore(_lease).Read() ?? throw new ReleaseValidationException("Missing prepared transaction.");
            oldProcess.AssertExited(_lease, initial);
            inputs = await ForwardUpdateInputs.VerifyAsync(_lease, operationId, context, _verifier, _discovery, _time,
                preflightDeadline.Token).ConfigureAwait(false);
            var record = inputs.Record;
            oldProcess.AssertExited(_lease, record);
            AssertSpace(scratchCreated: false);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.Validated);
            var scratch = Path.Combine(inputs.Operation, "forward-app.tmp");
            var displaced = Path.Combine(inputs.Operation, "displaced-original-app.exe");
            AssertAbsent(scratch); AssertAbsent(displaced);
            await inputs.Target.CopyToAsync(scratch, preflightDeadline.Token).ConfigureAwait(false);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.ScratchFlushed);
            if (RecoveryRestorer.Inspect(scratch, ManifestRules.MaximumFileSize) != record.TargetApp)
                throw new ReleaseValidationException("Forward scratch changed.");
            UpdatePayload.InspectExecutable(scratch, inputs.Manifest.Version, inputs.Manifest.CommitSha);
            inputs.AssertFresh(); inputs.AssertCurrentBaseline(); AssertSpace(scratchCreated: true);
            oldProcess.AssertExited(_lease, record, consume: true);
            preflightDeadline.Token.ThrowIfCancellationRequested();
            // No unsafe user cancellation after handoff is armed. Internal startup/cleanup deadlines remain.
            armed = true; // Also covers an exception after journal promotion but before Write returns.
            Save(RecoveryStep.Handoff);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.HandoffSaved);
            Save(RecoveryStep.Replacing);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.ReplacingSaved);
            inputs.AssertFresh(); inputs.AssertCurrentBaseline();
            AssertAbsent(displaced);
            var app = Path.Combine(_lease.Root, "app", "PrivacyMask.App.exe");
            StagingWorkspace.AssertSafe(scratch);
            File.Replace(scratch, app, displaced, ignoreMetadataErrors: false);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.PayloadReplaced);
            if (RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize) != record.TargetApp ||
                RecoveryRestorer.Inspect(displaced, ManifestRules.MaximumFileSize) != record.OriginalApp)
                throw new ReleaseValidationException("Forward replacement changed.");
            Save(RecoveryStep.AwaitingReadiness);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.AwaitingReadinessSaved);

            // Pin the exact installed target while CreateProcess and the authenticated exchange run.
            using var installed = await PreparationFile.OpenAsync(app, ManifestRules.MaximumFileSize, true, CancellationToken.None).ConfigureAwait(false);
            if (installed.Snapshot != record.TargetApp) throw new ReleaseValidationException("Launch payload changed.");
            UpdatePayload.InspectExecutable(app, inputs.Manifest.Version, inputs.Manifest.CommitSha);
            var session = new ReadinessSession(Guid.ParseExact(_lease.InstallId, "N"), Guid.ParseExact(operationId, "N"));
            server = new(session);
            readinessDeadline = new(_hooks?.ReadyTimeout ?? TimeSpan.FromSeconds(60));
            using var parent = Process.GetCurrentProcess();
            var self = Environment.ProcessPath ?? throw new ReleaseValidationException("Missing worker image.");
            var parentIdentity = PipeProcessIdentity.Capture(parent, self);
            var start = new ProcessStartInfo(app) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _lease.Root };
            foreach (var argument in new[] { "--update-start", server.PipeName, _lease.InstallId, operationId,
                parentIdentity.ProcessId.ToString(CultureInfo.InvariantCulture), parentIdentity.CreationUtcTicks.ToString(CultureInfo.InvariantCulture),
                startMinimized ? "minimized" : "normal" }) start.ArgumentList.Add(argument);
            child = Process.Start(start) ?? throw new IOException("Replacement did not start.");
            _hooks?.ChildStarted?.Invoke(child);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.ChildStarted);
            var identity = PipeProcessIdentity.Capture(child, app);
            exchange = server.ReceiveReadyAsync(identity, readinessDeadline.Token);
            var exited = exitObservation = child.WaitForExitAsync(readinessDeadline.Token);
            if (await Task.WhenAny(exchange, exited).ConfigureAwait(false) != exchange)
                throw new ReleaseValidationException("Replacement exited before readiness.");
            await exchange.ConfigureAwait(false);
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.ReadyReceived);
            var observation = Task.Delay(TimeSpan.FromMilliseconds(100), readinessDeadline.Token);
            if (await Task.WhenAny(exited, observation).ConfigureAwait(false) == exited)
                throw new ReleaseValidationException("Replacement exited during readiness.");
            await observation.ConfigureAwait(false);
            identity.ValidateLive();
            _lease.AssertHeld();
            if (RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize) != record.TargetApp)
                throw new ReleaseValidationException("Ready payload changed.");
            Save(RecoveryStep.Committed);
            committed = true;
            _hooks?.Checkpoint?.Invoke(TransactionCheckpoint.CommittedSaved);
            // The durable journal MUST precede the success receipt. A lost receipt never rolls back a commit.
            await server.AcceptAsync(identity, readinessDeadline.Token).ConfigureAwait(false);
            await server.ConfirmReceiptAsync(identity, readinessDeadline.Token).ConfigureAwait(false);
            return new(TransactionOutcome.Committed);

            void Save(RecoveryStep step)
            {
                record = record with { Step = step, Revision = record.Revision + 1 };
                new RecoveryJournalStore(_lease, point => {
                    // Mark durable commit inside the promotion callback, even if a later hook/IO throws.
                    if (step == RecoveryStep.Committed && point == JournalCheckpoint.Promoted) committed = true;
                    _hooks?.Journal?.Invoke(step, point);
                }).Write(record);
            }
            void AssertSpace(bool scratchCreated)
            {
                var required = PreparationBudget.Required(record.OriginalApp, record.SettingsPrimary, record.SettingsBackup, record.TargetApp, true);
                if (scratchCreated) required = checked(required - record.TargetApp.Size);
                var available = _availableSpace?.Invoke(_lease.Root) ?? new DriveInfo(Path.GetPathRoot(_lease.Root)!).AvailableFreeSpace;
                if (available < required) throw new ReleaseValidationException("Insufficient forward recovery space.");
            }
        }
        catch (OperationCanceledException) { reason = armed ? "readiness-timeout" : cancellationToken.IsCancellationRequested ? "transaction-canceled" : "preflight-timeout"; }
        catch (Exception error) when (error is ReleaseValidationException or JsonException or DecoderFallbackException or
            CryptographicException or FormatException or BadImageFormatException or InvalidDataException or OverflowException)
        { reason = "transaction-validation-failed"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or
            Win32Exception or InvalidOperationException)
        { reason = "transaction-io-failed"; }
        finally
        {
            readinessDeadline?.Cancel();
            server?.Dispose();
            if (exchange is not null) { try { await exchange.ConfigureAwait(false); } catch (Exception) { /* Observe canceled/failed exchange. */ } }
            if (exitObservation is not null) { try { await exitObservation.ConfigureAwait(false); } catch (Exception) { /* Observe the bounded exit race. */ } }
            if (child is not null && !committed)
            {
                try
                {
                    // Only the retained Process returned by our own CreateProcess, never a name or process tree.
                    if (!child.HasExited) child.Kill(entireProcessTree: false);
                    using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await child.WaitForExitAsync(stopDeadline.Token).ConfigureAwait(false);
                    stopped = child.HasExited;
                }
                catch (Exception) { stopped = false; }
            }
            child?.Dispose();
            inputs?.Dispose(); // Release pins before restoration; never touch settings while a replacement is alive.
            readinessDeadline?.Dispose();
        }
        if (committed) return new(TransactionOutcome.CommittedReceiptLost, "commit-receipt-unavailable");
        if (!armed) return new(TransactionOutcome.Rejected, reason);
        if (!stopped) return new(TransactionOutcome.RepairRequired, "replacement-exit-unconfirmed");
        // Disk recovery only. The future stable launcher must restart/qualify the old app separately.
        var recovery = new RecoveryRestorer(_lease).Recover();
        return recovery.Outcome == RecoveryOutcome.Recovered ? new(TransactionOutcome.RecoveredFiles, reason)
            : new(TransactionOutcome.RepairRequired, "transaction-recovery-incomplete");
    }

    private static void AssertAbsent(string path)
    {
        StagingWorkspace.AssertSafe(path);
        if (Path.Exists(path)) throw new ReleaseValidationException("Forward evidence collision.");
    }
}
