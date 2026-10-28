using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class MandatoryUpdateTests
{
    [Fact]
    public async Task StartupDownloadsAndInstallsWithoutAUserAction()
    {
        var host = new Host();
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "download", "install" }, host.Events);
        Assert.False(host.Status.CanCancelDownload);
        Assert.False(host.Status.CanDiscardDownload);
        Assert.False(host.Status.UserActionsAllowed);
    }

    [Fact]
    public async Task OfflineStartupWaitsAndReconnectRunsThePipeline()
    {
        var host = new Host { Online = false };
        await host.Controller.PulseAsync();
        Assert.Empty(host.Events);
        host.Online = true;
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "download", "install" }, host.Events);
    }

    [Theory]
    [InlineData("Panic")]
    [InlineData("unsaved or invalid settings")]
    [InlineData("Close or minimize")]
    public async Task SafetyDeferralKeepsTheStageAndResumesWithoutAnotherPrompt(string reason)
    {
        var host = new Host { Block = reason };
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "download" }, host.Events);
        Assert.Contains(reason, host.Status.DownloadText);
        Assert.Equal(TimeSpan.FromSeconds(10), host.Controller.NextDelay);
        await host.Controller.PulseAsync();
        Assert.Equal(2, host.Events.Count);
        host.Block = null;
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "download", "install" }, host.Events);
    }

    [Fact]
    public async Task DisabledActivationMakesNoConnectivityCheckOrWork()
    {
        var host = new Host { Enabled = false };
        await host.Controller.PulseAsync();
        Assert.Empty(host.Events);
        Assert.Equal(0, host.ConnectivityReads);
    }

    [Fact]
    public async Task ReconnectBurstsAreSingleFlightAndStopCannotStartADownload()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new Host { CheckDelay = hold.Task };
        var first = host.Controller.PulseAsync();
        Assert.Same(first, host.Controller.PulseAsync());
        host.Controller.Stop();
        hold.SetResult();
        await first;
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check" }, host.Events);
    }

    [Fact]
    public async Task SuccessfulNoUpdateCheckRunsAgainAfter24HoursWithoutRestart()
    {
        var host = new Host { Available = false };
        await host.Controller.PulseAsync();
        await host.Controller.PulseAsync();
        host.Clock.Now = host.Clock.Now.AddHours(23);
        await host.Controller.PulseAsync();
        Assert.Single(host.Events);
        host.Clock.Now = host.Clock.Now.AddHours(1);
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "check" }, host.Events);
    }

    [Fact]
    public async Task DownloadFailureRetriesAutomaticallyAfterTheCooldown()
    {
        var host = new Host { DownloadFails = true };
        await host.Controller.PulseAsync();
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "download" }, host.Events);
        host.Clock.Now = host.Clock.Now.AddMinutes(15);
        host.DownloadFails = false;
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "download", "check", "download", "install" }, host.Events);
    }

    [Fact]
    public async Task ServerDeadlineSurvivesReconnectHints()
    {
        var host = new Host { Available = false, Retry = TimeSpan.FromDays(2) };
        await host.Controller.PulseAsync();
        host.Clock.Now = host.Clock.Now.AddDays(1);
        await host.Controller.PulseAsync();
        Assert.Single(host.Events);
        host.Clock.Now = host.Clock.Now.AddDays(1);
        await host.Controller.PulseAsync();
        Assert.Equal(2, host.Events.Count);
    }

    [Fact]
    public async Task RepairStateNeverDeletesAStageOrStartsAnotherWorker()
    {
        var host = new Host();
        host.Status.FinishInstall("repair", repairRequired: true);
        await host.Controller.PulseAsync();
        Assert.Empty(host.Events);
    }

    [Fact]
    public async Task AcceptedInstallCanStopItsControllerWithoutWaitingOnItself()
    {
        var host = new Host();
        host.OnInstall = host.Controller.Stop;
        await host.Controller.PulseAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await host.Controller.PulseAsync();
        Assert.Equal(3, host.Events.Count);
    }

    [Fact]
    public async Task FailedPreparedIdentityIsRetiredAndFreshlyCheckedBeforeAnotherDownload()
    {
        var host = new Host();
        host.OnInstall = () => host.Status.FinishInstall("fresh download needed", requiresNewDownload: true);
        await host.Controller.PulseAsync();
        await host.Controller.PulseAsync();
        Assert.Equal(3, host.Events.Count);
        host.Clock.Now = host.Clock.Now.AddMinutes(15);
        host.OnInstall = null;
        await host.Controller.PulseAsync();
        Assert.Equal(new[] { "check", "download", "install", "retire", "check", "download", "install" }, host.Events);
        Assert.True(host.FreshCheck);
    }

    [Fact]
    public void PersistedOptOutAndChannelChangesCannotDisableRequiredPolicy()
    {
        var status = new UpdateStatusViewModel();
        status.RequireAutomaticUpdates();
        var saved = new DefaultSettingsFactory().Create();
        saved.AutomaticUpdateChecks = false;
        saved.UpdateChannel = status.RequiredChannel == UpdateReleaseChannel.Preview ? UpdateReleaseChannel.Stable : UpdateReleaseChannel.Preview;
        using var model = SettingsViewModel.FromModel(saved, RuntimeMode.Standard, "test", new HashSet<HotkeyAction>(), status);
        model.AutomaticUpdateChecks = false;
        model.UpdateChannel = saved.UpdateChannel;
        Assert.True(model.AutomaticUpdateChecks);
        Assert.Equal(status.RequiredChannel, model.UpdateChannel);
        Assert.True(model.ToModel().AutomaticUpdateChecks);
        Assert.Equal(status.RequiredChannel, model.ToModel().UpdateChannel);
        Assert.False(model.HasPendingChanges);
    }

    [Fact]
    public async Task DirectCancelAndDiscardRequestsCannotCancelRequiredDownload()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepare = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var discards = 0;
        await using var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, Candidate)),
            downloads: new(async (_, _, _, token) =>
            {
                using var registration = token.Register(() => cancelled = true);
                ready.SetResult();
                await prepare.Task;
            }, _ => { discards++; return Task.FromResult(true); }), mandatory: true);
        await session.CheckAsync(session.Status.RequiredChannel);
        var pending = session.DownloadAsync(session.Status.RequiredChannel);
        await ready.Task;
        session.CancelDownload();
        await session.DiscardDownloadAsync();
        Assert.False(cancelled);
        prepare.SetResult();
        await pending;
        await session.DiscardDownloadAsync();
        Assert.Equal(0, discards);
        Assert.True(session.Status.HasVerifiedDownload);
    }

    [Fact]
    public async Task MandatorySessionIgnoresOldOptOutAndReusesPersistedBuildCadence()
    {
        var store = new History();
        var clock = new DiscoveryClock();
        var calls = 0;
        async Task Run()
        {
            await using var session = new UpdateCheckSession((_, _) =>
            {
                calls++;
                return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));
            }, clock, store, mandatory: true);
            await session.CheckMandatoryAsync(() => new(false, session.Status.RequiredChannel));
        }
        await Run();
        await Run();
        Assert.Equal(1, calls);
        clock.Now += UpdateRuntimePolicy.SuccessfulCheckInterval - TimeSpan.FromSeconds(1);
        await Run();
        Assert.Equal(1, calls);
        clock.Now += TimeSpan.FromSeconds(1);
        await Run();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DurableRetryAndUnknownAdmissionsCannotBeBypassed()
    {
        var now = DateTimeOffset.UtcNow;
        var failure = new UpdateCheckHistory(LastAttemptUtc: now);
        Assert.Equal(now.AddMinutes(15), failure.MandatoryDueAt(now));
        Assert.Equal(now.AddDays(3), (failure with { RetryAfterUtc = now.AddDays(3) }).MandatoryDueAt(now));
        Assert.Equal(DateTimeOffset.MaxValue, (failure with { RequestPending = true }).MandatoryDueAt(now));
        Assert.Equal(now + UpdateRuntimePolicy.SuccessfulCheckInterval, (failure with { PreviewSuccessUtc = now }).MandatoryDueAt(now));
    }

    // Display/state-machine fixture only; no signed data, staging, process or network authority.
    private static readonly UpdateManifest Manifest = new(1, ManifestRules.AppId, ManifestRules.Repository,
        "2.0.0", "v2.0.0", new string('1', 40), "stable", "win-x64", 19041, 1,
        new(ManifestRules.ArchiveName("2.0.0"), 1024, new string('a', 64)),
        [new("PrivacyMask.App.exe", 2048, new string('b', 64))], new(1, 13, 13));
    private static readonly UpdateCandidate Candidate = new(10, 103, GitHubUrls.ReleasePage("v2.0.0"), GitHubUrls.AssetApi(103), Manifest, default, default, "");

    private sealed class Host
    {
        internal readonly UpdateStatusViewModel Status = new();
        internal readonly DiscoveryClock Clock = new();
        internal readonly List<string> Events = [];
        internal readonly MandatoryUpdateController Controller;
        internal bool Enabled = true, Online = true, Available = true, DownloadFails;
        internal int ConnectivityReads;
        internal bool FreshCheck;
        internal string? Block;
        internal TimeSpan? Retry;
        internal Task CheckDelay = Task.CompletedTask;
        internal Action? OnInstall;
        private DateTimeOffset? _next;
        internal Host()
        {
            Status.RequireAutomaticUpdates();
            Controller = new(Status, new(() => Enabled, () => { ConnectivityReads++; return Online; },
                async fresh =>
                {
                    FreshCheck = fresh;
                    Events.Add("check"); await CheckDelay;
                    Status.Complete(new(Available ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate, Available ? Candidate : null), Status.RequiredChannel, Clock.Now);
                    _next = Clock.Now + (Retry ?? TimeSpan.FromHours(24));
                }, () =>
                {
                    Events.Add("download");
                    if (DownloadFails)
                    {
                        Status.FinishDownload("fixture download failed");
                        throw new InvalidOperationException("Fixture failure, not shown to users.");
                    }
                    Status.FinishDownload("verified", ready: true);
                    return Task.CompletedTask;
                }, () => { Events.Add("retire"); Status.FinishDiscard(true); return Task.CompletedTask; },
                () => { Events.Add("install"); OnInstall?.Invoke(); return Task.CompletedTask; }, () => _next,
                () => Block is null ? null : new UpdateRestartBlock(Block == "Panic" ? UpdateRestartBlockKind.Panic
                    : Block == "unsaved or invalid settings" ? UpdateRestartBlockKind.SettingsEdits : UpdateRestartBlockKind.VisibleWindows,
                    [new("fixture", "Example app")])), Clock);
        }
    }

    private sealed class History : IUpdateCheckHistoryStore
    {
        private UpdateCheckHistory _value = new();
        private sealed class Lease : IDisposable { public void Dispose() { } }
        public IDisposable Acquire() => new Lease();
        public Task<UpdateCheckHistory> ReadAsync(CancellationToken token) => Task.FromResult(_value);
        public Task WriteAsync(UpdateCheckHistory value, CancellationToken token) { _value = value; return Task.CompletedTask; }
    }
}
