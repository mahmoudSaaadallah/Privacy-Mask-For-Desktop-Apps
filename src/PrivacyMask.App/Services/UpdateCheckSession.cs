using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

/// <summary>Dispatcher-owned, manual discovery only. No timers, settings writes, downloads or restart.</summary>
internal sealed class UpdateCheckSession : IAsyncDisposable
{
    private readonly Func<UpdateReleaseChannel, CancellationToken, Task<UpdateCheckResult>> _check;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _active;
    private bool _stopped;

    internal UpdateCheckSession(Func<UpdateReleaseChannel, CancellationToken, Task<UpdateCheckResult>> check,
        TimeProvider? time = null)
    {
        _check = check;
        _time = time ?? TimeProvider.System;
    }

    internal UpdateStatusViewModel Status { get; } = new();

    internal Task CheckAsync(UpdateReleaseChannel savedChannel)
    {
        if (_stopped) return Task.CompletedTask;
        if (_active is { IsCompleted: false }) return _active;
        return _active = RunAsync(savedChannel);
    }

    private async Task RunAsync(UpdateReleaseChannel channel)
    {
        Status.Begin();
        try
        {
            var result = await _check(channel, _lifetime.Token);
            if (!_stopped) Status.Complete(result, channel, _time.GetUtcNow());
        }
        catch (OperationCanceledException) when (_stopped) { }
        catch (Exception)
        {
            // Keep URLs, local paths, credentials and arbitrary server exception text out of the UI.
            if (!_stopped) Status.Complete(new(UpdateCheckStatus.UnableToCheck), channel, _time.GetUtcNow());
        }
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
        if (_active is not null) await _active;
        _lifetime.Dispose();
    }
}
