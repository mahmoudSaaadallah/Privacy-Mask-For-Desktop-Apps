namespace PrivacyMask.App.Services;

/// <summary>
/// Serializes complete shell mutations, not just file writes. Callbacks retain the
/// caller's synchronization context. They must not enqueue and await another callback.
/// </summary>
internal sealed class ShellOperationQueue
{
    internal const int MaximumPending = 32;
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private int _pending;
    private bool _stopping;
    private Suspension? _suspension;

    internal bool IsStopping { get { lock (_gate) return _stopping; } }
    internal bool IsSuspended { get { lock (_gate) return _suspension is not null; } }

    /// <summary>Reject new writers while accepted operations drain. Disposing never reopens a stopped queue.</summary>
    internal Suspension? TrySuspend()
    {
        lock (_gate)
        {
            if (_stopping || _suspension is not null) return null;
            return _suspension = new Suspension(this, _tail);
        }
    }

    internal sealed class Suspension(ShellOperationQueue owner, Task drain) : IDisposable
    {
        internal Task Drain { get; } = drain;
        public void Dispose()
        {
            lock (owner._gate)
                if (ReferenceEquals(owner._suspension, this)) owner._suspension = null;
        }
    }

    internal Task<bool> TryRunAsync(Func<Task> operation, bool requireIdle = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Task previous;
        TaskCompletionSource finished;
        lock (_gate)
        {
            if (_stopping || _suspension is not null || _pending >= MaximumPending || (requireIdle && _pending != 0))
                return Task.FromResult(false);
            previous = _tail;
            finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _tail = finished.Task;
            _pending++;
        }
        return ExecuteAsync(previous, finished, operation);
    }

    /// <summary>Closes admission atomically; accepted callbacks, including their continuations, drain first.</summary>
    internal Task StopAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            return _tail;
        }
    }

    private async Task<bool> ExecuteAsync(Task previous, TaskCompletionSource finished, Func<Task> operation)
    {
        try
        {
            await previous;
            await operation();
            return true;
        }
        finally
        {
            lock (_gate)
            {
                _pending--;
                // The drain tail never faults: each caller observes its own operation error.
                finished.SetResult();
            }
        }
    }
}
