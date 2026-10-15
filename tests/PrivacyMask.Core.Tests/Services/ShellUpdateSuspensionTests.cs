using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ShellUpdateSuspensionTests
{
    [Fact]
    public async Task Suspension_drains_accepted_callbacks_and_restores_admission_after_cancel()
    {
        var queue = new ShellOperationQueue();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = queue.TryRunAsync(() => finish.Task);
        var state = 0;
        var next = queue.TryRunAsync(() => { state++; return Task.CompletedTask; });
        using var suspension = queue.TrySuspend();
        Assert.NotNull(suspension);
        Assert.True(queue.IsSuspended);
        Assert.False(suspension.Drain.IsCompleted);
        Assert.Null(queue.TrySuspend());
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Late writer.")));
        finish.SetResult();
        await suspension.Drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await active);
        Assert.True(await next);
        Assert.Equal(1, state);
        suspension.Dispose();
        Assert.True(await queue.TryRunAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task Old_suspension_cannot_release_new_suspension_or_reopen_shutdown()
    {
        var queue = new ShellOperationQueue();
        var first = queue.TrySuspend()!;
        first.Dispose();
        using var second = queue.TrySuspend();
        first.Dispose();
        Assert.True(queue.IsSuspended);
        await queue.StopAsync();
        second!.Dispose();
        Assert.True(queue.IsStopping);
        Assert.Null(queue.TrySuspend());
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Stopped writer.")));
    }
}
