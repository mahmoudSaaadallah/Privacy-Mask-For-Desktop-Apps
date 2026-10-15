using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ApplicationShutdownTests
{
    [Fact]
    public async Task ExitAndSingletonRelease_RequireCompletedDisposal()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var sequence = new ApplicationShutdown(async () =>
        {
            events.Add("draining");
            await release.Task;
            events.Add("disposed");
        }, () => events.Add("singleton"), code => events.Add($"exit:{code}"));
        var shutdown = sequence.RunAsync(-1);
        Assert.True(sequence.IsRequested);
        Assert.Same(shutdown, sequence.RunAsync());
        Assert.Equal(new[] { "draining" }, events);
        Assert.False(shutdown.IsCompleted);
        release.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "draining", "disposed", "singleton", "exit:-1" }, events);
        Assert.Same(shutdown, sequence.RunAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDrain_DoesNotReleaseInstanceOrExit(bool synchronous)
    {
        var expected = new InvalidOperationException("Synthetic disposal failure.");
        var sequence = new ApplicationShutdown(() => synchronous ? throw expected : Task.FromException(expected),
            () => throw new Exception("Instance must remain held."), _ => throw new Exception("Must not exit."));
        var shutdown = sequence.RunAsync();
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => shutdown));
        Assert.Same(shutdown, sequence.RunAsync());
    }

    [Fact]
    public async Task SynchronousReentry_JoinsAlreadyPublishedSequence()
    {
        ApplicationShutdown? sequence = null;
        Task? reentrant = null;
        var exits = 0;
        sequence = new ApplicationShutdown(() =>
        {
            reentrant = sequence!.RunAsync(5);
            return Task.CompletedTask;
        }, () => { }, _ => exits++);
        var shutdown = sequence.RunAsync();
        await shutdown;
        Assert.Same(shutdown, reentrant);
        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task QueueDrain_KeepsResourcesUntilLastAcceptedStateUpdate()
    {
        var queue = new ShellOperationQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateUpdated = false;
        var disposed = false;
        var active = queue.TryRunAsync(async () => { await release.Task; Assert.False(disposed); stateUpdated = true; });
        var sequence = new ApplicationShutdown(async () => { await queue.StopAsync(); Assert.True(stateUpdated); disposed = true; },
            () => Assert.True(disposed), _ => Assert.True(disposed));
        var shutdown = sequence.RunAsync();
        Assert.False(disposed);
        Assert.False(await queue.TryRunAsync(() => throw new Exception("Late mutation.")));
        release.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await active);
    }

    [Fact]
    public async Task CanceledDrain_IsFailureNotPermissionToExit()
    {
        var sequence = new ApplicationShutdown(() => Task.FromCanceled(new CancellationToken(true)),
            () => throw new Exception("Do not release."), _ => throw new Exception("Do not exit."));
        Assert.False(sequence.IsRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sequence.RunAsync());
    }

    [Fact]
    public async Task FailedInstanceRelease_DoesNotCallExit()
    {
        var sequence = new ApplicationShutdown(() => Task.CompletedTask,
            () => throw new InvalidOperationException("Synthetic release failure."), _ => throw new Exception("Do not exit."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sequence.RunAsync());
    }
}
