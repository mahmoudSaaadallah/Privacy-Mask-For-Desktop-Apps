using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class PanicUpdatePreparationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifiedPinAndConfirmedMinimizationUnlockGlobalAndProfilePanic(bool global)
    {
        var host = new Host();
        var mode = new ProtectionStateMachine();
        var profiles = new ProfileProtectionState();
        profiles.TogglePanic("first");
        profiles.TogglePanic("second");
        if (global) mode.TogglePanic();
        host.OnMinimize = () =>
        {
            Assert.True(profiles.HasAnyPanic);
            Assert.Equal(global ? RuntimeMode.Panic : RuntimeMode.Standard, mode.CurrentMode);
        };
        host.OnUnlock = () =>
        {
            profiles.UnlockAllPanic();
            if (mode.CurrentMode == RuntimeMode.Panic) mode.TogglePanic();
        };

        Assert.Equal(PanicUpdateResult.Ready, await host.Preparation.RunAsync());
        Assert.Equal(new[] { "pin", "minimize", "unlock" }, host.Events);
        Assert.False(profiles.HasAnyPanic);
        Assert.Equal(RuntimeMode.Standard, mode.CurrentMode);
        Assert.False(host.Queue.IsSuspended);
    }

    [Fact]
    public async Task CancelDoesNotMinimizeOrUnlockAndDoesNotPromptEveryTimerTick()
    {
        var host = new Host { PinAccepted = false };
        Assert.Equal(PanicUpdateResult.Canceled, await host.Preparation.RunAsync());
        Assert.Equal(PanicUpdateResult.Deferred, await host.Preparation.RunAsync());
        Assert.True(host.Panic);
        Assert.Equal(new[] { "pin" }, host.Events);
        host.Preparation.AllowRetry();
        host.PinAccepted = true;
        Assert.Equal(PanicUpdateResult.Ready, await host.Preparation.RunAsync());
        Assert.Equal(new[] { "pin", "pin", "minimize", "unlock" }, host.Events);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("minimize")]
    public async Task ChangingTheOwnedStageRevokesConsentBeforeUnlock(string step)
    {
        var host = new Host();
        void Change() => host.Selection = host.Selection! with { OperationId = "another-stage" };
        if (step == "pin") host.OnPin = Change;
        else host.OnMinimize = Change;
        Assert.Equal(PanicUpdateResult.ContextChanged, await host.Preparation.RunAsync());
        Assert.True(host.Panic);
        Assert.DoesNotContain("unlock", host.Events);
    }

    [Fact]
    public async Task FailedMinimizationRetainsPanicAndNeedsExplicitRetry()
    {
        var host = new Host { Minimized = false };
        Assert.Equal(PanicUpdateResult.WindowsUnavailable, await host.Preparation.RunAsync());
        Assert.True(host.Panic);
        Assert.Equal(PanicUpdateResult.Deferred, await host.Preparation.RunAsync());
        Assert.Equal(new[] { "pin", "minimize" }, host.Events);
        Assert.True(await host.Queue.TryRunAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task AdmissionDrainsMutationsAndConcurrentAttemptsCannotOpenAnotherDialog()
    {
        var host = new Host();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = host.Queue.TryRunAsync(() => finish.Task);
        var first = host.Preparation.RunAsync();
        Assert.Empty(host.Events);
        Assert.Equal(PanicUpdateResult.Busy, await host.Preparation.RunAsync());
        Assert.False(await host.Queue.TryRunAsync(() => throw new Exception("Late mutation")));
        finish.SetResult();
        await writer;
        Assert.Equal(PanicUpdateResult.Ready, await first);
    }

    [Fact]
    public async Task StoppingDuringMinimizationKeepsPanicAndNeverUnlocks()
    {
        var host = new Host();
        host.OnMinimize = () => _ = host.Queue.StopAsync();
        Assert.Equal(PanicUpdateResult.ContextChanged, await host.Preparation.RunAsync());
        Assert.True(host.Panic);
        Assert.DoesNotContain("unlock", host.Events);
    }

    private sealed class Host
    {
        internal ShellOperationQueue Queue { get; } = new();
        internal List<string> Events { get; } = [];
        internal OwnedUpdateSelection? Selection = new("owned-stage", "2.0.0-beta.24", UpdateReleaseChannel.Preview);
        internal bool Panic = true, PinAccepted = true, Minimized = true;
        internal Action? OnPin, OnMinimize, OnUnlock;
        internal PanicUpdatePreparation Preparation { get; }

        internal Host() => Preparation = new(Queue, new(() => Selection, () => true, () => Panic,
            version => { Assert.Equal(Selection!.Version, version); Events.Add("pin"); OnPin?.Invoke(); return PinAccepted; },
            _ => { Events.Add("minimize"); OnMinimize?.Invoke(); return Task.FromResult(Minimized); },
            () => { Events.Add("unlock"); OnUnlock?.Invoke(); Panic = false; }));
    }
}
