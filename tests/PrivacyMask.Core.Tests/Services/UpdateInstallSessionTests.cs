using System.IO;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateInstallSessionTests
{
    private static OwnedUpdateSelection Selection => new(new string('a', 32), "2.0.0", UpdateReleaseChannel.Preview);
    private static UpdateStatusViewModel Ready()
    {
        var status = new UpdateStatusViewModel();
        status.FinishDownload("Verified download", ready: true);
        return status;
    }

    [Fact]
    public async Task MissingReadyStageTrustOrSavedChannelNeverInvokesApply()
    {
        var status = new UpdateStatusViewModel();
        var selects = 0;
        var calls = 0;
        var eligible = false;
        var session = new UpdateInstallSession(status, channel => { selects++; return channel == Selection.Channel ? Selection : null; },
            (_, _) => { calls++; return Task.FromResult(UpdateRestartResult.Canceled); }, _ => eligible);
        await session.StartAsync(Selection.Channel);
        status.FinishDownload("ready", ready: true);
        await session.StartAsync(Selection.Channel);
        Assert.Equal(0, selects);
        eligible = true;
        await session.StartAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(0, calls);
        Assert.True(status.CanDiscardDownload);
        await session.StopAsync();
    }

    [Theory]
    [InlineData((int)UpdateRestartResult.PanicLocked, "PIN")]
    [InlineData((int)UpdateRestartResult.SettingsChanged, "Settings changed")]
    [InlineData((int)UpdateRestartResult.Canceled, "cancelled")]
    [InlineData((int)UpdateRestartResult.Busy, "in progress")]
    public async Task RefusedRestartRetainsDownloadAndReportsTheActualGate(int outcome, string text)
    {
        var status = Ready();
        var session = new UpdateInstallSession(status, _ => Selection, (_, _) => Task.FromResult((UpdateRestartResult)outcome), _ => true);
        await session.StartAsync(Selection.Channel);
        Assert.Contains(text, status.DownloadText);
        Assert.True(status.CanInstall);
        Assert.True(status.CanDiscardDownload);
        Assert.False(status.IsInstalling);
        await session.StopAsync();
    }

    [Fact]
    public async Task ReentrantAndRepeatedClicksJoinOneApplyAndBlockDiscard()
    {
        var status = Ready();
        var finish = new TaskCompletionSource<UpdateRestartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        UpdateInstallSession? session = null;
        Task? reentered = null;
        session = new(status, _ => Selection, (selection, _) =>
        {
            calls++;
            Assert.Equal(Selection, selection);
            return finish.Task;
        }, _ => true);
        status.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(status.IsInstalling) && status.IsInstalling)
                reentered = session.StartAsync(Selection.Channel);
        };
        var first = session.StartAsync(Selection.Channel);
        Assert.Same(first, reentered);
        Assert.Same(first, session.StartAsync(Selection.Channel));
        Assert.False(status.CanDiscardDownload);
        Assert.False(status.CanInstall);
        finish.SetResult(UpdateRestartResult.Canceled);
        await first;
        Assert.Equal(1, calls);
        await session.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsafeCleanupBlocksAllActionsWhileSafeFailureRequiresANewDownload(bool unsafeCleanup)
    {
        var status = Ready();
        var session = new UpdateInstallSession(status, _ => Selection, (_, _) => unsafeCleanup
            ? throw new UnsafeUpdateResumeException() : throw new IOException("secret path and token"), _ => true);
        await session.StartAsync(Selection.Channel);
        Assert.False(status.CanInstall);
        Assert.Equal(!unsafeCleanup, status.CanDiscardDownload);
        Assert.DoesNotContain("secret", status.DownloadText);
        await session.StopAsync();
    }

    [Fact]
    public async Task OrdinaryExitCancelsAndWaitsForCleanupRatherThanGrantingRestartConsent()
    {
        var status = Ready();
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new UpdateInstallSession(status, _ => Selection, async (_, token) =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled.SetResult(); await cleanup.Task; throw; }
            return UpdateRestartResult.HandedOff;
        }, _ => true);
        var active = session.StartAsync(Selection.Channel);
        var stop = session.StopAsync();
        await cancelled.Task;
        Assert.False(stop.IsCompleted);
        cleanup.SetResult();
        await stop;
        await active;
        await session.StartAsync(Selection.Channel);
        Assert.DoesNotContain("Restarting", status.DownloadText);
    }

    [Fact]
    public async Task AcceptedHandoffAllowsItsOwnShutdownToDrainWithoutSelfDeadlockOrCancellation()
    {
        var status = Ready();
        UpdateInstallSession? session = null;
        var cancelledAfterAcceptance = false;
        session = new(status, _ => Selection, async (_, token) =>
        {
            await Task.Yield();
            session!.AcceptHandoff();
            await session.StopAsync(); // Exact shape of App RequestShutdown -> Shell Dispose.
            cancelledAfterAcceptance = token.IsCancellationRequested;
            return UpdateRestartResult.HandedOff;
        }, _ => true);
        await session.StartAsync(Selection.Channel).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(cancelledAfterAcceptance);
        Assert.Contains("Restarting", status.DownloadText);
        Assert.Contains("not yet confirmed", status.DownloadText);
        Assert.False(status.CanInstall);
        Assert.False(status.CanDiscardDownload);
    }

    [Fact]
    public async Task StopRacingAcceptanceStillHasABoundedSafeDrain()
    {
        var status = Ready();
        var accept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateInstallSession? session = null;
        session = new(status, _ => Selection, async (_, _) =>
        {
            await accept.Task; // Transport has already accepted: it must finish even if cancellation raced.
            session!.AcceptHandoff();
            await session.StopAsync();
            return UpdateRestartResult.HandedOff;
        }, _ => true);
        var active = session.StartAsync(Selection.Channel);
        var stop = session.StopAsync();
        Assert.False(stop.IsCompleted);
        accept.SetResult();
        await Task.WhenAll(active, stop).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Restarting", status.DownloadText);
    }
}
