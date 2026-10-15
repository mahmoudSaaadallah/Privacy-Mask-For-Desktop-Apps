namespace PrivacyMask.App.Services;

internal enum UpdateRestartResult { HandedOff, Busy, Canceled, PanicLocked, SettingsChanged }
internal sealed class UnsafeUpdateResumeException : Exception;

internal sealed record UpdateRestartActions(
    Func<bool> HasPanic, Func<bool> HasEdits, Func<bool> IsClosing,
    Func<Task<bool>> ResolveEdits, Func<bool> ConfirmRestart,
    Func<Task> FreezeUi, Action ThawUi, Func<Task> Shutdown);

/// <summary>Dispatcher-affine restart gates. No operation ID, journal or ordinary Exit grants consent.</summary>
internal sealed class UpdateRestartCoordinator(ShellOperationQueue operations, UpdateRestartActions host)
{
    private int _busy;
    internal bool IsBusy => Volatile.Read(ref _busy) != 0;

    internal async Task<UpdateRestartResult> RunAsync(
        Func<Action, CancellationToken, Task> handoff, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return UpdateRestartResult.Busy;
        ShellOperationQueue.Suspension? suspension = null;
        var frozen = false;
        var handedOff = false;
        var safeToResume = true;
        try
        {
            if (host.IsClosing() || operations.IsStopping) return UpdateRestartResult.Busy;
            if (host.HasPanic()) return UpdateRestartResult.PanicLocked;
            cancellationToken.ThrowIfCancellationRequested();
            if (!await host.ResolveEdits()) return UpdateRestartResult.Canceled;
            cancellationToken.ThrowIfCancellationRequested();
            if (host.HasPanic()) return UpdateRestartResult.PanicLocked;
            if (!host.ConfirmRestart()) return UpdateRestartResult.Canceled;
            cancellationToken.ThrowIfCancellationRequested();
            suspension = operations.TrySuspend();
            if (suspension is null) return UpdateRestartResult.Busy;
            frozen = true;
            var uiDrain = host.FreezeUi();
            // Never cancel an admitted save or its UI finally continuation halfway through.
            await Task.WhenAll(suspension.Drain, uiDrain);
            cancellationToken.ThrowIfCancellationRequested();
            if (host.IsClosing()) return UpdateRestartResult.Busy;
            if (host.HasPanic()) return UpdateRestartResult.PanicLocked;
            if (host.HasEdits()) return UpdateRestartResult.SettingsChanged;
            await handoff(AssertReady, cancellationToken);
            handedOff = true;
            await operations.StopAsync();
            await host.Shutdown();
            return UpdateRestartResult.HandedOff;
        }
        catch (UnsafeUpdateResumeException)
        {
            safeToResume = false;
            // Keep masks/resources alive, but never write over unresolved prepared state.
            await operations.StopAsync();
            throw;
        }
        finally
        {
            if (safeToResume && !handedOff)
            {
                suspension?.Dispose();
                if (frozen) host.ThawUi();
            }
            Volatile.Write(ref _busy, 0);
        }

        void AssertReady()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!operations.IsSuspended || host.IsClosing() || host.HasPanic() || host.HasEdits())
                throw new InvalidOperationException("Restart conditions changed. Check protection and saved settings before trying again.");
        }
    }
}
