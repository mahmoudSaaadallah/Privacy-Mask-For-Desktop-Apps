using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal sealed record UpdateCheckPreferences(bool Automatic, UpdateReleaseChannel Channel);

/// <summary>Dispatcher-owned single-flight discovery; the required monitor schedules build-specific checks.</summary>
internal sealed class UpdateCheckSession : IAsyncDisposable
{
    private readonly Func<UpdateReleaseChannel, CancellationToken, Task<UpdateCheckResult>> _check;
    private readonly Func<UpdateReleaseChannel, UpdateCheckResult?>? _preflight;
    private readonly IUpdateCheckHistoryStore? _history;
    private readonly UpdateDownloadActions? _downloads;
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
    private readonly bool _mandatory;
    private readonly TimeSpan _successfulCheckInterval;
    internal DateTimeOffset? NextAutomaticAttemptUtc { get; private set; }

    internal UpdateCheckSession(Func<UpdateReleaseChannel, CancellationToken, Task<UpdateCheckResult>> check,
        TimeProvider? time = null, IUpdateCheckHistoryStore? history = null,
        Func<UpdateReleaseChannel, UpdateCheckResult?>? preflight = null,
        Func<CancellationToken, Task>? startupDelay = null, UpdateDownloadActions? downloads = null, bool mandatory = false,
        TimeSpan? successfulCheckInterval = null)
    {
        _check = check;
        _time = time ?? TimeProvider.System;
        _history = history;
        _downloads = downloads;
        _mandatory = mandatory;
        _successfulCheckInterval = successfulCheckInterval ?? UpdateRuntimePolicy.SuccessfulCheckInterval;
        if (_successfulCheckInterval <= TimeSpan.Zero || _successfulCheckInterval > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(successfulCheckInterval));
        if (mandatory) Status.RequireAutomaticUpdates();
        _preflight = preflight;
        _startupDelay = startupDelay ?? (token => Task.Delay(TimeSpan.FromSeconds(30), _time, token));
    }

    internal UpdateStatusViewModel Status { get; } = new();
    internal Task CheckAsync(UpdateReleaseChannel savedChannel) => StartCheck(savedChannel, automatic: false);

    internal Task CheckMandatoryAsync(Func<UpdateCheckPreferences> preferences)
    {
        if (!_mandatory) throw new InvalidOperationException("Required update policy is not active.");
        _preferences = preferences;
        return StartCheck(preferences().Channel, automatic: true);
    }

    internal Task DownloadAsync(UpdateReleaseChannel savedChannel)
    {
        if (_downloads is null || _stopped || _paused) return Task.CompletedTask;
        if (_active is { IsCompleted: false }) return _active;
        if (!Status.CanDownload || Status.Candidate is not { } candidate) return Task.CompletedTask;
        if (Status.CandidateChannel != savedChannel || Status.CandidateCheckedUtc is not { } checkedAt ||
            checkedAt > _time.GetUtcNow() || _time.GetUtcNow() - checkedAt > TimeSpan.FromHours(24))
        {
            Status.Begin();
            Status.Complete(new(UpdateCheckStatus.Incomplete), savedChannel, _time.GetUtcNow());
            return Task.CompletedTask;
        }
        return Admit(savedChannel, automatic: false, candidate);
    }

    internal void CancelDownload()
    {
        if (!_mandatory && Status.IsDownloading) _activeCancellation?.Cancel();
    }

    internal Task DiscardDownloadAsync() => _mandatory ? Task.CompletedTask : DiscardOwnedAsync();

    // Only the controller may retire an unusable owned stage for a fresh retry.
    internal Task RetireFailedDownloadAsync() => Status.RequiresNewDownload ? DiscardOwnedAsync() : Task.CompletedTask;

    private Task DiscardOwnedAsync()
    {
        if (_downloads is null || !Status.CanRetireDownload || _active is { IsCompleted: false }) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _active = completion.Task;
        _activeAutomatic = false;
        _activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = DiscardAsync(_activeCancellation, completion);
        return _active;
    }

    private async Task DiscardAsync(CancellationTokenSource cancellation, TaskCompletionSource completion)
    {
        try
        {
            Status.BeginDiscard();
            var removed = await _downloads!.Discard(cancellation.Token);
            if (!_stopped) Status.FinishDiscard(removed);
        }
        catch (Exception)
        {
            if (!_stopped) Status.FinishDiscard(removed: false);
        }
        finally
        {
            _activeCancellation = null;
            cancellation.Dispose();
            completion.TrySetResult();
        }
    }

    internal Task ScheduleStartupCheck(Func<UpdateCheckPreferences> savedPreferences)
    {
        if (_startupScheduled || _stopped || _paused) return _startup ?? Task.CompletedTask;
        _startupScheduled = true;
        _preferences = savedPreferences;
        try
        {
            var saved = savedPreferences();
            // No trust, opt-out or unsupported root: no delay timer and no history IO.
            if ((!_mandatory && !saved.Automatic) || _preflight?.Invoke(saved.Channel) is not null) return Task.CompletedTask;
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
            if (!_stopped && !_paused && (_mandatory || saved.Automatic)) await StartCheck(saved.Channel, automatic: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { /* No background modal, noisy retry or polling loop. */ }
        finally { completion.TrySetResult(); }
    }

    internal void PreferencesChanged()
    {
        if (_stopped || _preferences is null) return;
        var saved = _preferences();
        if (!_mandatory && !saved.Automatic) _startupCancellation?.Cancel();
        if (_activeAutomatic && ((!_mandatory && !saved.Automatic) || saved.Channel != _activeChannel)) _activeCancellation?.Cancel();
    }

    private Task StartCheck(UpdateReleaseChannel channel, bool automatic)
    {
        if (_stopped || _paused) return Task.CompletedTask;
        if (_active is { IsCompleted: false }) return automatic ? Task.CompletedTask : _active;
        if (!Status.CanCheck) return Task.CompletedTask;
        return Admit(channel, automatic);
    }

    private Task Admit(UpdateReleaseChannel channel, bool automatic, UpdateCandidate? download = null)
    {
        // Admit before notifying bindings, so reentrant callers join this exact operation.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _active = completion.Task;
        _activeAutomatic = automatic;
        _activeChannel = channel;
        _activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = RunAsync(channel, automatic, _activeCancellation, completion, download);
        return _active;
    }

    private async Task RunAsync(UpdateReleaseChannel channel, bool automatic,
        CancellationTokenSource cancellation, TaskCompletionSource completion, UpdateCandidate? download)
    {
        var token = cancellation.Token;
        var historyIo = false;
        UpdateCheckHistory? admittedHistory = null;
        IDisposable? historyLease = null;
        var acceptingProgress = true;
        try
        {
            if (download is not null) Status.BeginDownload();
            else if (!automatic) Status.Begin();
            if (_preflight?.Invoke(channel) is { } unavailable)
            {
                if (!automatic) Status.Complete(unavailable, channel, _time.GetUtcNow());
                if (download is not null) Status.FinishDownload("Download did not start. Use View releases.");
                return;
            }
            historyIo = _history is not null;
            historyLease = _history?.Acquire();
            var history = _history is null ? new UpdateCheckHistory() : await _history.ReadAsync(token);
            NextAutomaticAttemptUtc = history.MandatoryDueAt(_time.GetUtcNow(), _successfulCheckInterval);
            if (history.RequestPending) throw new InvalidOperationException("Previous request outcome requires inspection.");
            historyIo = false;
            var now = _time.GetUtcNow();
            Status.LoadHistory(history, channel);
            if (history.RetryAfterUtc > now)
            {
                if (!automatic) Status.Complete(new(UpdateCheckStatus.RateLimited, RetryAt: history.RetryAfterUtc), channel, now);
                if (download is not null) Status.FinishDownload("Download did not start. Wait until the retry time above.");
                return;
            }
            if (automatic && (!AutomaticAllowed(channel) || (_mandatory ? history.MandatoryDueAt(now, _successfulCheckInterval) > now : !history.StartupDue(now)))) return;
            history = history.Attempt(now);
            NextAutomaticAttemptUtc = history.MandatoryDueAt(now, _successfulCheckInterval);
            // Persist admission BEFORE HTTP; unknown outcomes cannot bypass the retry/history gate.
            historyIo = _history is not null;
            if (_history is not null) await _history.WriteAsync(history, token);
            historyIo = false;
            admittedHistory = history;
            token.ThrowIfCancellationRequested();
            if (automatic && !AutomaticAllowed(channel)) return;
            if (automatic) Status.Begin();
            UpdateCheckResult result;
            if (download is null) result = await _check(channel, token);
            else
            {
                var progress = new Progress<UpdateDownloadProgress>(value =>
                {
                    if (acceptingProgress && !token.IsCancellationRequested) Status.DownloadProgress(value);
                });
                try { await _downloads!.Prepare(download, channel, progress, token); }
                catch (UpdateDownloadException failure)
                {
                    // Even download responses share the durable repository-wide server retry gate.
                    history = history.Complete((UpdateChannel)channel, new(UpdateCheckStatus.UnableToCheck, RetryAt: failure.RetryAt), _time.GetUtcNow());
                    admittedHistory = history;
                    NextAutomaticAttemptUtc = history.MandatoryDueAt(_time.GetUtcNow(), _successfulCheckInterval);
                    historyIo = _history is not null;
                    using var failureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    if (_history is not null) await _history.WriteAsync(history, failureDeadline.Token);
                    admittedHistory = null;
                    historyIo = false;
                    if (!_stopped)
                    {
                        if (failure.RetryAt is not null) Status.Complete(new(UpdateCheckStatus.RateLimited, RetryAt: history.RetryAfterUtc), channel, _time.GetUtcNow());
                        Status.FinishDownload("Download failed. Your installed application and protection are unchanged.", cleanupIncomplete: failure.CleanupIncomplete);
                    }
                    return;
                }
                result = new(UpdateCheckStatus.Available, download);
            }
            history = download is null ? history.Complete((UpdateChannel)channel, result, _time.GetUtcNow()) : history with { RequestPending = false };
            NextAutomaticAttemptUtc = history.MandatoryDueAt(_time.GetUtcNow(), _successfulCheckInterval);
            admittedHistory = history;
            historyIo = _history is not null;
            // Once a response exists, persist its retry information even if opt-out/shutdown raced it.
            using var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (_history is not null) await _history.WriteAsync(history, completionDeadline.Token);
            historyIo = false;
            admittedHistory = null;
            if (!_stopped)
            {
                if (download is not null) Status.FinishDownload("Verified download ready. Choose Update now to review restart consent, or discard to free space. Nothing has been installed.", ready: true);
                else if (token.IsCancellationRequested) Status.Cancel();
                else Status.Complete(result, channel, _time.GetUtcNow());
            }
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested)
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
            if (!_stopped)
            {
                if (download is not null) Status.FinishDownload("Download cancelled. Your installed application and protection are unchanged.",
                    cleanupIncomplete: error.Data["UpdateCleanupIncomplete"] is true);
                else Status.Cancel();
            }
        }
        catch (Exception)
        {
            if (!_stopped) Status.Complete(new(UpdateCheckStatus.UnableToCheck,
                Reason: _history is not null && (historyIo || admittedHistory is not null)
                    ? "local-check-history-unavailable" : null), channel, _time.GetUtcNow());
            if (!_stopped && download is not null)
                Status.FinishDownload("Download could not be completed safely. No update was installed.", cleanupIncomplete: true);
        }
        finally
        {
            acceptingProgress = false;
            historyLease?.Dispose();
            _activeCancellation = null;
            cancellation.Dispose();
            completion.TrySetResult();
        }
    }

    private bool AutomaticAllowed(UpdateReleaseChannel channel) => !_stopped && !_paused
        && _preferences?.Invoke() is { } saved && (_mandatory || saved.Automatic) && saved.Channel == channel;

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
