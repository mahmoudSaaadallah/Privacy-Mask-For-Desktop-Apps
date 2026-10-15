namespace PrivacyMask.App.Services;

/// <summary>UI-thread, single-flight shutdown. An exit callback is never an asynchronous drain acknowledgement.</summary>
internal sealed class ApplicationShutdown(Func<Task> drainAndDispose, Action releaseInstance, Action<int> exit)
{
    private TaskCompletionSource? _completion;
    internal bool IsRequested => _completion is not null;

    internal Task RunAsync(int exitCode = 0)
    {
        if (_completion is not null) return _completion.Task;
        // Publish before invoking callbacks: synchronous/reentrant exit requests
        // must join the same sequence rather than disposing the shell twice.
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ExecuteAsync(exitCode);
        return _completion.Task;
    }

    private async Task ExecuteAsync(int exitCode)
    {
        try
        {
            await drainAndDispose();
            releaseInstance();
            exit(exitCode);
            _completion!.SetResult();
        }
        catch (Exception exception)
        {
            // Fail closed: a failed drain does not release the instance or
            // claim a clean exit. The caller must observe/report this failure.
            _completion!.SetException(exception);
        }
    }
}
