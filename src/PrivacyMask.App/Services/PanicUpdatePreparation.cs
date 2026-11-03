namespace PrivacyMask.App.Services;

internal enum PanicUpdateResult { Ready, Busy, Deferred, Canceled, WindowsUnavailable, ContextChanged }

internal sealed record PanicUpdateActions(Func<OwnedUpdateSelection?> Selection, Func<bool> CanPrepare,
    Func<bool> HasPanic, Func<string, bool> VerifyPin,
    Func<Func<bool>, Task<bool>> MinimizeWindows, Action Unlock);

/// <summary>PIN consent belongs to one verified stage and one attempt. Masks remain until minimization succeeds.</summary>
internal sealed class PanicUpdatePreparation(ShellOperationQueue operations, PanicUpdateActions host)
{
    private OwnedUpdateSelection? _prompted;
    private bool _busy;

    internal void AllowRetry()
    {
        if (!_busy) _prompted = null;
    }

    internal async Task<PanicUpdateResult> RunAsync()
    {
        if (_busy || !host.CanPrepare() || host.Selection() is not { } selection)
            return PanicUpdateResult.Busy;
        if (!host.HasPanic()) return PanicUpdateResult.Ready;
        if (_prompted == selection) return PanicUpdateResult.Deferred;
        using var suspension = operations.TrySuspend();
        if (suspension is null) return PanicUpdateResult.Busy;
        _busy = true;
        try
        {
            await suspension.Drain;
            if (!IsCurrent()) return PanicUpdateResult.ContextChanged;
            if (!host.HasPanic()) return PanicUpdateResult.Ready;
            _prompted = selection;
            if (!host.VerifyPin(selection.Version)) return PanicUpdateResult.Canceled;
            if (!IsCurrent()) return PanicUpdateResult.ContextChanged;
            if (!await host.MinimizeWindows(IsCurrent)) return PanicUpdateResult.WindowsUnavailable;
            if (!IsCurrent()) return PanicUpdateResult.ContextChanged;
            host.Unlock();
            return PanicUpdateResult.Ready;
        }
        finally { _busy = false; }

        bool IsCurrent() => !operations.IsStopping && host.CanPrepare() && host.Selection() == selection;
    }
}
