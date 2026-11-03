using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class TrayExitGuardTests
{
    [Fact]
    public async Task Panic_blocks_exit_even_when_the_queue_was_stopped_for_repair()
    {
        var operations = new ShellOperationQueue();
        await operations.StopAsync();
        var reports = 0;

        await TrayExitGuard.TryExitAsync(operations, () => true, () => reports++,
            () => throw new Exception("Panic must keep the shell alive."));

        Assert.Equal(1, reports);
    }

    [Fact]
    public async Task Admitted_panic_activation_blocks_exit_and_leaves_unlock_available()
    {
        var operations = new ShellOperationQueue();
        var finishPanic = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var panic = false;
        var exits = 0;
        var reports = 0;
        var activation = operations.TryRunAsync(async () => { await finishPanic.Task; panic = true; });
        Task TryExit() => TrayExitGuard.TryExitAsync(operations, () => panic, () => reports++,
            async () => { exits++; await operations.StopAsync(); });

        var firstExit = TryExit();
        Assert.False(firstExit.IsCompleted);
        Assert.False(await operations.TryRunAsync(() => throw new Exception("Exit must freeze new actions.")));
        finishPanic.SetResult();
        Assert.True(await activation);
        await firstExit;
        Assert.Equal(0, exits);
        Assert.Equal(1, reports);

        Assert.True(await operations.TryRunAsync(() => { panic = false; return Task.CompletedTask; }));
        await TryExit();
        Assert.Equal(1, exits);
        Assert.True(operations.IsStopping);
    }
}
