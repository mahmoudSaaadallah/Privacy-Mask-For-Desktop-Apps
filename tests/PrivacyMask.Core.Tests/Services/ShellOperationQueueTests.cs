using System.IO;
using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ShellOperationQueueTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task CompleteCallbacks_AreFifoIncludingPostWriteState()
    {
        var queue = new ShellOperationQueue();
        var release = Signal();
        var events = new List<int>();
        var first = queue.TryRunAsync(async () => { events.Add(1); await release.Task; events.Add(2); });
        var second = queue.TryRunAsync(() => { events.Add(3); return Task.CompletedTask; });
        var third = queue.TryRunAsync(() => { events.Add(4); return Task.CompletedTask; });
        Assert.Equal(new[] { 1 }, events);
        release.SetResult();
        Assert.All(await Task.WhenAll(first, second, third), Assert.True);
        Assert.Equal(new[] { 1, 2, 3, 4 }, events);
    }

    [Fact]
    public async Task Stop_ClosesAdmissionBeforeDrainingAndIsIdempotent()
    {
        var queue = new ShellOperationQueue();
        var release = Signal();
        var first = queue.TryRunAsync(() => release.Task);
        var secondRan = false;
        var second = queue.TryRunAsync(() => { secondRan = true; return Task.CompletedTask; });
        var drain = queue.StopAsync();
        Assert.Same(drain, queue.StopAsync());
        Assert.True(queue.IsStopping);
        Assert.False(drain.IsCompleted);
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Must not execute.")));
        release.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await first);
        Assert.True(await second);
        Assert.True(secondRan);
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Must not execute.")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_IsObservedByCallerButDoesNotPoisonQueueOrDrain(bool synchronous)
    {
        var queue = new ShellOperationQueue();
        var release = Signal();
        var expected = new IOException("Synthetic write failure.");
        var first = queue.TryRunAsync(() => release.Task);
        var failing = queue.TryRunAsync(() => synchronous ? throw expected : Task.FromException(expected));
        var after = queue.TryRunAsync(() => Task.CompletedTask);
        var drain = queue.StopAsync();
        release.SetResult();
        Assert.Same(expected, await Assert.ThrowsAsync<IOException>(() => failing));
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await first);
        Assert.True(await after);
    }

    [Fact]
    public async Task CanceledCallback_DoesNotCancelAcceptedSuccessor()
    {
        var queue = new ShellOperationQueue();
        var canceled = queue.TryRunAsync(() => Task.FromCanceled(new CancellationToken(true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(await queue.TryRunAsync(() => Task.CompletedTask));
        await queue.StopAsync();
    }

    [Fact]
    public async Task SaveAdmission_RejectsBusySnapshotWithoutExecutingIt()
    {
        var queue = new ShellOperationQueue();
        var release = Signal();
        var mutation = queue.TryRunAsync(() => release.Task);
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Stale save."), requireIdle: true));
        release.SetResult();
        Assert.True(await mutation);
        Assert.True(await queue.TryRunAsync(() => Task.CompletedTask, requireIdle: true));
    }

    [Fact]
    public async Task KeyRepeatBacklog_IsBoundedAndCapacityIsRecovered()
    {
        var queue = new ShellOperationQueue();
        var release = Signal();
        var accepted = new List<Task<bool>> { queue.TryRunAsync(() => release.Task) };
        for (var i = 1; i < ShellOperationQueue.MaximumPending; i++)
            accepted.Add(queue.TryRunAsync(() => Task.CompletedTask));
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Over capacity.")));
        release.SetResult();
        Assert.All(await Task.WhenAll(accepted), Assert.True);
        Assert.True(await queue.TryRunAsync(() => Task.CompletedTask));
        await queue.StopAsync();
    }

    [Fact]
    public async Task EmptyQueue_CanStopWithoutStartingAnyWork()
    {
        var queue = new ShellOperationQueue();
        Assert.True(queue.StopAsync().IsCompletedSuccessfully);
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Stopped.")));
    }

    [Fact]
    public void NullCallback_IsRejectedWithoutConsumingCapacity()
    {
        var queue = new ShellOperationQueue();
        Assert.Throws<ArgumentNullException>(() => { _ = queue.TryRunAsync(null!); });
        Assert.True(queue.StopAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CompetingAdmissionAndStop_NeverRunAfterDrain()
    {
        var queue = new ShellOperationQueue();
        var count = 0;
        using var ready = new Barrier(3);
        var producer = Task.Run(async () =>
        {
            ready.SignalAndWait();
            return await queue.TryRunAsync(async () => { await Task.Yield(); Interlocked.Increment(ref count); });
        });
        var stopper = Task.Run(async () => { ready.SignalAndWait(); await queue.StopAsync(); });
        ready.SignalAndWait();
        await stopper.WaitAsync(TimeSpan.FromSeconds(5));
        var atDrain = Volatile.Read(ref count);
        var accepted = await producer;
        Assert.Equal(accepted ? 1 : 0, atDrain);
        Assert.Equal(atDrain, count);
    }
}
