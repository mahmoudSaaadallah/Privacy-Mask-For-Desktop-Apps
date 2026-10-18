using PrivacyMask.App.ViewModels;

namespace PrivacyMask.App.Services;

internal sealed record MandatoryUpdateActions(Func<bool> Enabled, Func<bool> Online,
    Func<bool, Task> Check, Func<Task> Download, Func<Task> RetireFailedStage, Func<Task> Install,
    Func<DateTimeOffset?> NextCheckUtc, Func<string?> RestartBlockReason);

/// <summary>Dispatcher-owned required update pipeline; lifecycle stop is not a user opt-out.</summary>
internal sealed class MandatoryUpdateController(UpdateStatusViewModel status, MandatoryUpdateActions host, TimeProvider? clock = null)
{
    private readonly TimeProvider _time = clock ?? TimeProvider.System;
    private Task? _active;
    private bool _stopped;
    private DateTimeOffset? _nextNetworkAttempt;
    internal bool Enabled => !_stopped && host.Enabled();

    internal TimeSpan NextDelay
    {
        get
        {
            if (status.HasVerifiedDownload && !status.RequiresNewDownload && !status.NeedsRepair) return TimeSpan.FromSeconds(10);
            var delay = (_nextNetworkAttempt ?? _time.GetUtcNow().AddMinutes(15)) - _time.GetUtcNow();
            return TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 60, 86400));
        }
    }

    internal Task PulseAsync()
    {
        if (_stopped) return Task.CompletedTask;
        if (_active is { IsCompleted: false }) return _active;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _active = completion.Task;
        _ = RunAsync(completion);
        return _active;
    }

    private async Task RunAsync(TaskCompletionSource completion)
    {
        try
        {
            if (!Enabled || status.IsChecking || status.IsInstalling || status.NeedsRepair) return;
            if (!host.Online())
            {
                status.ShowAutomaticWait("Required update retained. Waiting for network connectivity; it cannot be cancelled.");
                return;
            }
            if (status.HasVerifiedDownload && !status.RequiresNewDownload) { await TryInstallAsync(); return; }
            if (_nextNetworkAttempt > _time.GetUtcNow()) return;
            var freshCheck = status.RequiresNewDownload;
            if (freshCheck)
            {
                await host.RetireFailedStage();
                if (_stopped || status.HasVerifiedDownload || status.NeedsRepair) return;
            }
            if (!status.CanDownload)
            {
                await host.Check(freshCheck);
                _nextNetworkAttempt = host.NextCheckUtc() ?? _time.GetUtcNow().AddMinutes(15);
            }
            if (_stopped || !Enabled) return;
            if (status.CanDownload)
            {
                await host.Download();
                _nextNetworkAttempt = host.NextCheckUtc() ?? _time.GetUtcNow().AddMinutes(15);
            }
            if (!_stopped && status.CanInstall) await TryInstallAsync();
        }
        catch (Exception)
        {
            _nextNetworkAttempt = _time.GetUtcNow().AddMinutes(15);
            status.ShowAutomaticWait("Required update could not finish safely. It will retry automatically; the current application is unchanged.");
        }
        finally { completion.TrySetResult(); }
    }

    private async Task TryInstallAsync()
    {
        if (_stopped || !Enabled || !status.CanInstall) return;
        if (host.RestartBlockReason() is { } reason) { status.ShowAutomaticWait(reason); return; }
        await host.Install();
        if (status.RequiresNewDownload) _nextNetworkAttempt = _time.GetUtcNow().AddMinutes(15);
    }

    // Never await our own install's shutdown. Shell disposal separately drains the
    // check session and the install session's accepted/safe-to-close boundary.
    internal void Stop() => _stopped = true;
}
