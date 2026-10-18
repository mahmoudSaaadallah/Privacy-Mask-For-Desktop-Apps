using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal sealed record UpdateCheckPreferences(bool Automatic, UpdateReleaseChannel Channel);

/// <summary>Dispatcher-owned discovery: at most one delayed startup check, never a recurring poller.</summary>
internal sealed class UpdateCheckSession : IAsyncDisposable
{
    private readonly Func<UpdateReleaseChannel, CancellationToken, Task<UpdateCheckResult>> _check;
    private readonly Func<UpdateReleaseChannel, UpdateCheckResult?>? _preflight;
    private readonly IUpdateCheckHistoryStore? _history;
    private readonly TimeProvider _time;
    private readonly Func<CancellationToken, Task> _startupDelay;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _activeCancellation;
    private CancellationTokenSource? _startupCancellation;
    private Func<UpdateCheckPreferences>? _preferences;
    private Task? _active;
    private Task? _startup;
    private bool _activeAutomatic;
    private UpdateReleaseChannel _activeChannel;
    private bool _startupScheduled;
    private bool _stopped;
    private bool _paused;

    internal UpdateCheckSession(Func<UpdateReleaseChannel, CancellationToken, Task<UpdateCheckResult>> check,
        TimeProvider? time = null, IUpdateCheckHistoryStore? history = null,
        Func<UpdateReleaseChannel, UpdateCheckResult?>? preflight = null,
        Func<CancellationToken, Task>? startupDelay = null)
    {
        _check = check;
        _time = time ?? TimeProvider.System;
        _history = history;
        _preflight = preflight;
        _startupDelay = startupDelay ?? (token => Task.Delay(TimeSpan.FromSeconds(30), _time, token));
    }

    internal UpdateStatusViewModel Status { get; } = new();
    internal Task CheckAsync(UpdateReleaseChannel savedChannel) => StartCheck(savedChannel, automatic: false);

    internal Task ScheduleStartupCheck(Func<UpdateCheckPreferences> savedPreferences)
    {
        if (_startupScheduled || _stopped || _paused) return _startup ?? Task.CompletedTask;
        _startupScheduled = true;
        _preferences = savedPreferences;
        try
        {
            var saved = savedPreferences();
            // No trust, opt-out or unsupported root: no delay timer and no history IO.
            if (!saved.Automatic || _preflight?.Invoke(saved.Channel) is not null) return Task.CompletedTask;
        }
        catch (Exception) { return Task.CompletedTask; } // Optional discovery never blocks healthy startup.
        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _startup = completion.Task;
        _ = RunStartupAsync(_startupCancellation.Token, completion);
        return _startup;
    }

    private async Task RunStartupAsync(CancellationToken token, TaskCompletionSource completion)
    {
        try
        {
            await _startupDelay(token);
            token.ThrowIfCancellationRequested();
            var saved = _preferences!();
            if (!_stopped && !_paused && saved.Automatic) await StartCheck(saved.Channel, automatic: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { /* No background modal, noisy retry or polling loop. */ }
        finally { completion.TrySetResult(); }
    }

    internal void PreferencesChanged()
    {
        if (_stopped || _preferences is null) return;
        var saved = _preferences();
        if (!saved.Automatic) _startupCancellation?.Cancel();
        if (_activeAutomatic && (!saved.Automatic || saved.Channel != _activeChannel)) _activeCancellation?.Cancel();
    }

    private Task StartCheck(UpdateReleaseChannel channel, bool automatic)
    {
        if (_stopped || _paused) return Task.CompletedTask;
        if (_active is { IsCompleted: false }) return automatic ? Task.CompletedTask : _active;
        // Admit before notifying bindings, so reentrant callers join this exact operation.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _active = completion.Task;
        _activeAutomatic = automatic;
        _activeChannel = channel;
        _activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = RunAsync(channel, automatic, _activeCancellation, completion);
        return _active;
    }

    private async Task RunAsync(UpdateReleaseChannel channel, bool automatic,
        CancellationTokenSource cancellation, TaskCompletionSource completion)
    {
        var token = cancellation.Token;
        var historyIo = false;
        UpdateCheckHistory? admittedHistory = null;
        IDisposable? historyLease = null;
        try
        {
            if (!automatic) Status.Begin();
            if (_preflight?.Invoke(channel) is { } unavailable)
            {
                if (!automatic) Status.Complete(unavailable, channel, _time.GetUtcNow());
                return;
            }
            historyIo = _history is not null;
            historyLease = _history?.Acquire();
            var history = _history is null ? new UpdateCheckHistory() : await _history.ReadAsync(token);
            if (history.RequestPending) throw new InvalidOperationException("Previous request outcome requires inspection.");
            historyIo = false;
            var now = _time.GetUtcNow();
            Status.LoadHistory(history, channel);
            if (history.RetryAfterUtc > now)
            {
                if (!automatic) Status.Complete(new(UpdateCheckStatus.RateLimited, RetryAt: history.RetryAfterUtc), channel, now);
                return;
            }
            if (automatic && (!AutomaticAllowed(channel) || !history.StartupDue(now))) return;
            history = history.Attempt(now);
            // Persist admission BEFORE HTTP; interrupted failures still count toward the 24-hour limit.
            historyIo = _history is not null;
            if (_history is not null) await _history.WriteAsync(history, token);
            historyIo = false;
            admittedHistory = history;
            token.ThrowIfCancellationRequested();
            if (automatic && !AutomaticAllowed(channel)) return;
            if (automatic) Status.Begin();
            var result = await _check(channel, token);
            history = history.Complete(channel, result, _time.GetUtcNow());
            admittedHistory = history;
            historyIo = _history is not null;
            // Once a response exists, persist its retry information even if opt-out/shutdown raced it.
            using var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (_history is not null) await _history.WriteAsync(history, completionDeadline.Token);
            historyIo = false;
            admittedHistory = null;
            if (!_stopped)
            {
                if (token.IsCancellationRequested) Status.Cancel();
                else Status.Complete(result, channel, _time.GetUtcNow());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal cancellation may retire our own admission. Failed/unknown writes stay fail-closed.
            if (_history is not null && admittedHistory is not null)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _history.WriteAsync(admittedHistory with { RequestPending = false }, cleanup.Token);
                }
                catch (Exception) { /* Preserve the pending outcome/scratch; never clear it speculatively. */ }
            }
            if (!_stopped) Status.Cancel();
        }
        catch (Exception)
        {
            if (!_stopped) Status.Complete(new(UpdateCheckStatus.UnableToCheck,
                Reason: _history is not null && (historyIo || admittedHistory is not null)
                    ? "local-check-history-unavailable" : null), channel, _time.GetUtcNow());
        }
        finally
        {
            historyLease?.Dispose();
            _activeCancellation = null;
            cancellation.Dispose();
            completion.TrySetResult();
        }
    }

    private bool AutomaticAllowed(UpdateReleaseChannel channel) => !_stopped && !_paused
        && _preferences?.Invoke() is { Automatic: true } saved && saved.Channel == channel;

    internal async Task SuspendAsync()
    {
        _paused = true;
        _startupCancellation?.Cancel();
        _activeCancellation?.Cancel();
        Status.SetPaused(true);
        await Task.WhenAll(_active ?? Task.CompletedTask, _startup ?? Task.CompletedTask);
    }

    internal void Resume()
    {
        if (_stopped) return;
        _paused = false;
        Status.SetPaused(false);
    }

    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _lifetime.Cancel();
        Status.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await Task.WhenAll(_active ?? Task.CompletedTask, _startup ?? Task.CompletedTask);
        _startupCancellation?.Dispose();
        _lifetime.Dispose();
    }
}
