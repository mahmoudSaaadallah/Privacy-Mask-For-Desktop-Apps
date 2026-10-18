using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.Services;

internal sealed record OwnedUpdateSelection(string OperationId, string Version, UpdateReleaseChannel Channel);

/// <summary>Dispatcher-owned apply request. Its accepted handoff boundary is deliberately not its own shutdown task.</summary>
internal sealed class UpdateInstallSession(UpdateStatusViewModel status,
    Func<UpdateReleaseChannel, OwnedUpdateSelection?> select,
    Func<OwnedUpdateSelection, CancellationToken, Task<UpdateRestartResult>> apply,
    Func<UpdateReleaseChannel, bool> eligible)
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _active;
    private TaskCompletionSource? _safeToClose;
    private bool _accepted;
    private bool _stopped;
    private bool _disposed;

    internal Task StartAsync(UpdateReleaseChannel savedChannel)
    {
        if (_active is { IsCompleted: false }) return _active;
        if (_stopped || !status.CanInstall) return Task.CompletedTask;
        OwnedUpdateSelection? selection;
        try { selection = eligible(savedChannel) ? select(savedChannel) : null; }
        catch (Exception) { selection = null; }
        if (selection is null)
        {
            status.FinishInstall("This download cannot be installed on the saved channel. Use View releases or discard and check again.");
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _safeToClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _active = completion.Task; // Admit before synchronous binding/event reentrancy.
        _ = RunAsync(selection, completion);
        return _active;
    }

    private async Task RunAsync(OwnedUpdateSelection selection, TaskCompletionSource completion)
    {
        try
        {
            status.BeginInstall();
            var result = await apply(selection, _lifetime.Token);
            if (_accepted || _stopped) return;
            status.FinishInstall(result switch
            {
                UpdateRestartResult.PanicLocked => "Unlock panic protection using your PIN before installing. Your download is retained.",
                UpdateRestartResult.ProtectedWindowsVisible => "Required update is waiting for protected application windows to be closed or minimized. Your download is retained.",
                UpdateRestartResult.SettingsChanged => "Settings changed. Resolve pending edits before restart. Your download is retained.",
                UpdateRestartResult.Canceled => "Installation cancelled. Your download is retained; protection is unchanged.",
                UpdateRestartResult.Busy => "Another action is in progress. Your download is retained.",
                _ => "Installation could not confirm a safe restart. Close sensitive windows and restart PrivacyMask to recover.",
            }, repairRequired: result == UpdateRestartResult.HandedOff);
        }
        catch (UnsafeUpdateResumeException)
        {
            if (!_accepted && !_stopped) status.FinishInstall("Update recovery needs attention. Close sensitive windows, exit PrivacyMask and start it again. Settings stay paused.", repairRequired: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Ordinary shutdown awaits safe cleanup; it does not grant handoff consent.
        }
        catch (Exception)
        {
            if (!_accepted && !_stopped) status.FinishInstall(status.IsMandatory
                ? "Installation could not proceed safely. Your current application is unchanged. A fresh required download will be attempted automatically."
                : "Installation could not proceed safely. Your current application is unchanged. Discard this download and check again, or use View releases.", requiresNewDownload: true);
        }
        finally
        {
            _safeToClose!.TrySetResult();
            if (_stopped) DisposeLifetime();
            completion.TrySetResult();
        }
    }

    internal void AcceptHandoff()
    {
        if (_active is null || _active.IsCompleted || _accepted) throw new InvalidOperationException("No active update handoff.");
        _accepted = true;
        status.ShowRestarting();
        // RequestShutdown drains writers/UI, not the task that is requesting that very shutdown.
        _safeToClose!.TrySetResult();
    }

    internal Task StopAsync()
    {
        if (!_stopped)
        {
            _stopped = true;
            if (!_accepted) _lifetime.Cancel();
            if (_active is null || _active.IsCompleted) DisposeLifetime();
        }
        return _safeToClose?.Task ?? Task.CompletedTask;
    }

    private void DisposeLifetime()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Dispose();
    }
}
