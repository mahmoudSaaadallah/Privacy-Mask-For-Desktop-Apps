using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateRestartCoordinatorTests
{
    [Fact]
    public async Task Accepted_writer_and_ui_continuation_finish_before_handoff_and_shutdown()
    {
        var fixture = new Host();
        var save = Signal();
        var continuation = Signal();
        var writer = fixture.Queue.TryRunAsync(async () => { await save.Task; fixture.Events.Add("saved"); });
        fixture.UiDrain = continuation.Task;
        var run = fixture.Coordinator.RunAsync(async (check, _) =>
        {
            check();
            fixture.Events.Add("handoff");
            Assert.False(await fixture.Queue.TryRunAsync(() => throw new Exception("Late write.")));
        });
        Assert.True(fixture.Frozen);
        Assert.False(run.IsCompleted);
        save.SetResult();
        Assert.True(await writer);
        Assert.DoesNotContain("handoff", fixture.Events);
        continuation.SetResult();
        Assert.Equal(UpdateRestartResult.HandedOff, await run);
        Assert.True(fixture.Queue.IsStopping);
        Assert.Equal(new[] { "edits", "consent", "freeze", "saved", "handoff", "shutdown" }, fixture.Events);
    }

    [Theory]
    [InlineData("panic", (int)UpdateRestartResult.PanicLocked)]
    [InlineData("edit-cancel", (int)UpdateRestartResult.Canceled)]
    [InlineData("consent-cancel", (int)UpdateRestartResult.Canceled)]
    [InlineData("closing", (int)UpdateRestartResult.Busy)]
    public async Task Rejected_user_gates_never_start_a_worker(string change, int expected)
    {
        var fixture = new Host { Panic = change == "panic", Resolve = change != "edit-cancel",
            Consent = change != "consent-cancel", Closing = change == "closing" };
        Assert.Equal((UpdateRestartResult)expected, await fixture.Coordinator.RunAsync((_, _) => throw new Exception("Must not hand off.")));
        Assert.False(fixture.Frozen);
        Assert.True(await fixture.Queue.TryRunAsync(() => Task.CompletedTask));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Panic_or_edits_appearing_during_drain_prevent_preparation(bool panic)
    {
        var fixture = new Host();
        var finish = Signal();
        var pending = fixture.Queue.TryRunAsync(async () => { await finish.Task; fixture.Panic = panic; fixture.Edits = !panic; });
        var run = fixture.Coordinator.RunAsync((_, _) => throw new Exception("Must not hand off."));
        finish.SetResult();
        await pending;
        Assert.Equal(panic ? UpdateRestartResult.PanicLocked : UpdateRestartResult.SettingsChanged, await run);
        Assert.False(fixture.Frozen);
        Assert.False(fixture.Queue.IsSuspended);
    }

    [Fact]
    public async Task Final_recheck_rejects_changed_state_and_safe_cleanup_resumes_writers()
    {
        var fixture = new Host();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.RunAsync((check, _) =>
        {
            fixture.Panic = true;
            check();
            throw new Exception("Must not accept.");
        }));
        Assert.False(fixture.Frozen);
        Assert.True(await fixture.Queue.TryRunAsync(() => Task.CompletedTask));
        Assert.DoesNotContain("shutdown", fixture.Events);
    }

    [Fact]
    public async Task Cancellation_waits_for_the_admitted_write_then_restores_ui_and_admission()
    {
        var fixture = new Host();
        var finish = Signal();
        var write = fixture.Queue.TryRunAsync(() => finish.Task);
        using var cancel = new CancellationTokenSource();
        var run = fixture.Coordinator.RunAsync((_, _) => throw new Exception("Canceled worker."), cancel.Token);
        cancel.Cancel();
        Assert.True(fixture.Frozen);
        Assert.False(run.IsCompleted);
        finish.SetResult();
        await write;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.False(fixture.Frozen);
        Assert.True(await fixture.Queue.TryRunAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task Concurrent_apply_is_single_flight_and_failed_cleanup_keeps_writers_closed()
    {
        var fixture = new Host();
        var fail = Signal();
        var first = fixture.Coordinator.RunAsync(async (_, _) => { await fail.Task; throw new UnsafeUpdateResumeException(); });
        Assert.Equal(UpdateRestartResult.Busy, await fixture.Coordinator.RunAsync((_, _) => throw new Exception("Second worker.")));
        fail.SetResult();
        await Assert.ThrowsAsync<UnsafeUpdateResumeException>(() => first);
        Assert.True(fixture.Frozen);
        Assert.True(fixture.Queue.IsStopping);
        Assert.DoesNotContain("shutdown", fixture.Events);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Host
    {
        internal ShellOperationQueue Queue { get; } = new();
        internal List<string> Events { get; } = [];
        internal bool Panic, Edits, Closing, Frozen;
        internal bool Resolve = true, Consent = true;
        internal Task UiDrain = Task.CompletedTask;
        internal UpdateRestartCoordinator Coordinator { get; }
        internal Host() => Coordinator = new(Queue, new(() => Panic, () => Edits, () => Closing,
            () => { Events.Add("edits"); return Task.FromResult(Resolve); },
            () => { Events.Add("consent"); return Consent; },
            () => { Frozen = true; Events.Add("freeze"); return UiDrain; },
            () => { Frozen = false; Events.Add("thaw"); },
            () => { Events.Add("shutdown"); return Task.CompletedTask; }));
    }
}
