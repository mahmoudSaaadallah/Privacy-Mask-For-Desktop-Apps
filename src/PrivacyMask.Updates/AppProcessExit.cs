using System.Diagnostics;

namespace PrivacyMask.Updates;

/// <summary>Owns a retained OS process handle captured while the exact installed app is alive. Never sends shutdown or kills it.</summary>
internal sealed class AppProcessExit : IDisposable
{
    private readonly Process _process;
    private readonly string _root;
    private readonly RecoveryJournal _record;
    private bool _observedExit;
    private bool _disposed;
    private int _consumed;

    private AppProcessExit(Process process, string root, RecoveryJournal record)
    { _process = process; _root = root; _record = record; }

    internal static AppProcessExit Capture(ManagedInstallLease lease, string operationId, int processId)
    {
        lease.AssertHeld();
        var record = new RecoveryJournalStore(lease).Read();
        if (!RecoveryRules.IsId(operationId) || record is null || record.OperationId != operationId || record.Step != RecoveryStep.Prepared)
            throw new ReleaseValidationException("Process handoff requires the exact prepared operation.");
        var path = Path.Combine(lease.Root, "app", "PrivacyMask.App.exe");
        var process = Process.GetProcessById(processId);
        try
        {
            // Capture verifies PID + creation time + canonical image path before exit; keep this handle,
            // not a subsequent process lookup that could refer to a recycled PID.
            _ = PipeProcessIdentity.Capture(process, path);
            if (RecoveryRestorer.Inspect(path, ManifestRules.MaximumFileSize) != record.OriginalApp)
                throw new ReleaseValidationException("Handoff payload changed.");
            return new(process, lease.Root, record);
        }
        catch { process.Dispose(); throw; }
    }

    internal async Task WaitAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (timeout < TimeSpan.FromMilliseconds(250) || timeout > TimeSpan.FromMinutes(1))
            throw new ReleaseValidationException("Invalid graceful exit deadline.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try { await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("Graceful exit deadline expired."); }
        if (!_process.HasExited || _process.ExitCode != 0) throw new ReleaseValidationException("Installed process did not exit cleanly.");
        _observedExit = true;
    }

    internal void AssertExited(ManagedInstallLease lease, RecoveryJournal record, bool consume = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lease.AssertHeld();
        if (!_observedExit || !_process.HasExited || _process.SafeHandle.IsClosed ||
            !string.Equals(_root, lease.Root, StringComparison.OrdinalIgnoreCase) ||
            record with { Revision = _record.Revision, Step = _record.Step } != _record ||
            Volatile.Read(ref _consumed) != 0)
            throw new ReleaseValidationException("Missing or stale exact-process exit evidence.");
        if (consume && Interlocked.CompareExchange(ref _consumed, 1, 0) != 0)
            throw new ReleaseValidationException("Process exit evidence already consumed.");
    }

    public void Dispose() { if (!_disposed) { _disposed = true; _process.Dispose(); } }
}
