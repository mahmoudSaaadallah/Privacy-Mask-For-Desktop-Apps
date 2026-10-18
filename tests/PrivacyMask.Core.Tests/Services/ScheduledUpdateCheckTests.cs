using System.IO;
using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ScheduledUpdateCheckTests
{
    private static UpdateCheckPreferences Enabled => new(true, UpdateReleaseChannel.Preview);
    private static Task NoDelay(CancellationToken token) => Task.CompletedTask;

    [Fact]
    public async Task OneStartupAttemptPersistsBeforeRequestEvenWhenOfflineAndSurvivesRestart()
    {
        var clock = new DiscoveryClock();
        var store = new MemoryHistory();
        var calls = 0;
        Task<UpdateCheckResult> Check(UpdateReleaseChannel channel, CancellationToken token)
        {
            Assert.Equal(clock.Now, store.Value.LastAttemptUtc);
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UnableToCheck));
        }
        await using (var first = new UpdateCheckSession(Check, clock, store, startupDelay: NoDelay))
        {
            await first.ScheduleStartupCheck(() => Enabled);
            clock.Now += TimeSpan.FromHours(23);
            await first.ScheduleStartupCheck(() => Enabled);
            Assert.Equal(1, calls);
        }
        await using (var restarted = new UpdateCheckSession(Check, clock, store, startupDelay: NoDelay))
            await restarted.ScheduleStartupCheck(() => Enabled);
        Assert.Equal(1, calls);
        clock.Now += TimeSpan.FromHours(1);
        await using (var due = new UpdateCheckSession(Check, clock, store, startupDelay: NoDelay))
            await due.ScheduleStartupCheck(() => Enabled);
        Assert.Equal(2, calls);
        Assert.Null(store.Value.PreviewSuccessUtc);
    }

    [Fact]
    public async Task UnconfiguredProductionCreatesNoTimerNoHistoryIoAndNoRequest()
    {
        using var runtime = new RuntimeUpdateDiscovery("\0invalid");
        var store = new MemoryHistory();
        var delays = 0;
        await using var session = new UpdateCheckSession(runtime.CheckAsync, history: store,
            preflight: runtime.Preflight, startupDelay: _ => { delays++; return Task.CompletedTask; });
        await session.ScheduleStartupCheck(() => Enabled);
        await session.CheckAsync(UpdateReleaseChannel.Preview);
        Assert.Equal(0, delays);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Writes);
        Assert.Contains("not configured", session.Status.StatusText);
    }

    [Fact]
    public async Task OptOutCreatesNoTimerButStillAllowsExplicitManualCheck()
    {
        var calls = 0;
        var delays = 0;
        var store = new MemoryHistory();
        await using var session = new UpdateCheckSession((_, _) =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));
        }, history: store, startupDelay: _ => { delays++; return Task.CompletedTask; });
        await session.ScheduleStartupCheck(() => new(false, UpdateReleaseChannel.Stable));
        Assert.Equal(0, delays);
        Assert.Equal(0, store.Reads);
        await session.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(1, calls);
        Assert.NotNull(store.Value.StableSuccessUtc);
    }

    [Fact]
    public async Task ManualCannotBypassPersistedGlobalRetryByChangingChannelOrRestarting()
    {
        var clock = new DiscoveryClock();
        var store = new MemoryHistory();
        var calls = 0;
        await using (var first = new UpdateCheckSession((_, _) =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.RateLimited, RetryAt: clock.Now.AddHours(2)));
        }, clock, store)) await first.CheckAsync(UpdateReleaseChannel.Preview);
        await using var restarted = new UpdateCheckSession((_, _) =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));
        }, clock, store);
        await restarted.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(1, calls);
        Assert.Contains("wait", restarted.Status.StatusText);
        clock.Now += TimeSpan.FromHours(2);
        await restarted.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SavedOptOutCancelsStartupDelayBeforeReadingHistory()
    {
        var preferences = Enabled;
        var delay = new TaskCompletionSource();
        var store = new MemoryHistory();
        await using var session = new UpdateCheckSession((_, _) => throw new Exception("Must not request"), history: store,
            startupDelay: token => delay.Task.WaitAsync(token));
        var pending = session.ScheduleStartupCheck(() => preferences);
        preferences = preferences with { Automatic = false };
        session.PreferencesChanged();
        await pending;
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task RecheckOptOutAfterDurableAdmissionPreventsHttp()
    {
        var preferences = Enabled;
        var store = new MemoryHistory();
        var calls = 0;
        await using var session = new UpdateCheckSession((_, _) =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));
        }, history: store, startupDelay: NoDelay);
        store.AfterWrite = () => { preferences = preferences with { Automatic = false }; session.PreferencesChanged(); };
        await session.ScheduleStartupCheck(() => preferences);
        Assert.Equal(0, calls);
        Assert.NotNull(store.Value.LastAttemptUtc);
        Assert.Null(store.Value.PreviewSuccessUtc);
        Assert.False(store.Value.RequestPending);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoryReadOrAdmissionWriteFailureSendsNoRequest(bool failRead)
    {
        var store = new MemoryHistory { FailRead = failRead, FailWrite = !failRead };
        var calls = 0;
        await using var session = new UpdateCheckSession((_, _) =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));
        }, history: store);
        await session.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(0, calls);
        Assert.Contains("history is unavailable", session.Status.StatusText);
        Assert.DoesNotContain("up to date", session.Status.StatusText);
    }

    [Fact]
    public async Task SuspendCancelsAndDrainsChecksAndResumeDoesNotRearmStartup()
    {
        var calls = 0;
        var entered = new TaskCompletionSource();
        await using var session = new UpdateCheckSession(async (_, token) =>
        {
            calls++;
            if (calls == 1) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new(UpdateCheckStatus.UpToDate);
        }, history: new MemoryHistory(), startupDelay: NoDelay);
        var startup = session.ScheduleStartupCheck(() => Enabled);
        await entered.Task;
        await session.SuspendAsync();
        await startup;
        await session.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(1, calls);
        Assert.False(session.Status.CanCheck);
        session.Resume();
        await session.ScheduleStartupCheck(() => Enabled);
        Assert.Equal(1, calls);
        await session.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SynchronousStatusEventsCannotReenterAdmission()
    {
        var calls = 0;
        var response = new TaskCompletionSource<UpdateCheckResult>();
        await using var session = new UpdateCheckSession((_, token) => { calls++; return response.Task.WaitAsync(token); });
        Task? reentrant = null;
        var entered = false;
        session.Status.PropertyChanged += (_, _) =>
        {
            if (entered) return;
            entered = true;
            reentrant = session.CheckAsync(UpdateReleaseChannel.Preview);
        };
        var first = session.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Same(first, reentrant);
        Assert.Equal(1, calls);
        response.SetResult(new(UpdateCheckStatus.UpToDate));
        await first;
    }

    [Fact]
    public async Task LostResultPersistenceKeepsPendingIntentAndBlocksRestartedManualRequests()
    {
        var store = new MemoryHistory { FailWriteAt = 2 };
        var calls = 0;
        Task<UpdateCheckResult> Request(UpdateReleaseChannel channel, CancellationToken token)
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.RateLimited, RetryAt: DateTimeOffset.UtcNow.AddDays(2)));
        }
        await using (var first = new UpdateCheckSession(Request, history: store))
            await first.CheckAsync(UpdateReleaseChannel.Preview);
        Assert.True(store.Value.RequestPending);
        await using var restarted = new UpdateCheckSession(Request, history: store);
        await restarted.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Equal(1, calls);
        Assert.Contains("history is unavailable", restarted.Status.StatusText);
    }

    [Fact]
    public async Task OptOutRacingReceivedRetryStillPersistsTheServerDeadline()
    {
        var clock = new DiscoveryClock();
        var store = new MemoryHistory();
        var preferences = Enabled;
        UpdateCheckSession? session = null;
        session = new UpdateCheckSession((_, _) =>
        {
            preferences = preferences with { Automatic = false };
            session!.PreferencesChanged();
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.RateLimited, RetryAt: clock.Now.AddDays(2)));
        }, clock, store, startupDelay: NoDelay);
        await using (session) await session.ScheduleStartupCheck(() => preferences);
        Assert.Equal(clock.Now.AddDays(2), store.Value.RetryAfterUtc);
        Assert.False(store.Value.RequestPending);
    }

    [Fact]
    public async Task ProductionDelayIsOneShotThirtySecondsAndShutdownReleasesItsTimer()
    {
        var time = new RecordingTime();
        await using var session = new UpdateCheckSession((_, _) => throw new Exception("No request before delay"), time);
        var scheduled = session.ScheduleStartupCheck(() => Enabled);
        Assert.False(scheduled.IsCompleted);
        Assert.Equal(TimeSpan.FromSeconds(30), time.Due);
        Assert.Equal(Timeout.InfiniteTimeSpan, time.Period);
        await session.SuspendAsync();
        await scheduled;
        Assert.True(time.Timer.Disposed);
        session.Resume();
        await session.ScheduleStartupCheck(() => Enabled);
        Assert.Equal(1, time.Created);
    }

    private sealed class RecordingTime : TimeProvider
    {
        internal TimeSpan Due;
        internal TimeSpan Period;
        internal int Created;
        internal RecordingTimer Timer = new();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Created++; Due = dueTime; Period = period;
            return Timer;
        }
    }

    private sealed class RecordingTimer : ITimer
    {
        internal bool Disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class MemoryHistory : IUpdateCheckHistoryStore
    {
        public IDisposable Acquire() => new MemoryLease();
        internal UpdateCheckHistory Value = new();
        internal int Reads;
        internal int Writes;
        internal bool FailRead;
        internal bool FailWrite;
        internal int FailWriteAt;
        internal Action? AfterWrite;
        private sealed class MemoryLease : IDisposable { public void Dispose() { } }
        public Task<UpdateCheckHistory> ReadAsync(CancellationToken token)
        {
            Reads++;
            if (FailRead) throw new IOException("private path never shown");
            return Task.FromResult(Value);
        }
        public Task WriteAsync(UpdateCheckHistory history, CancellationToken token)
        {
            Writes++;
            if (FailWrite || Writes == FailWriteAt) throw new IOException("private path never shown");
            Value = history;
            AfterWrite?.Invoke();
            return Task.CompletedTask;
        }
    }
}
