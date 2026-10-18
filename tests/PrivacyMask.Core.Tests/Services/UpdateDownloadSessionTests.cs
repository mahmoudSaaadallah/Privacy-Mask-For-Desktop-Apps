using System.IO;
using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateDownloadSessionTests
{
    private const UpdateReleaseChannel Preview = UpdateReleaseChannel.Preview;
    private static Task<bool> Discard(CancellationToken _) => Task.FromResult(true);

    [Fact]
    public async Task UnconfiguredRuntimeCannotInspectAnInvalidPathOrStage()
    {
        using var discovery = new RuntimeUpdateDiscovery("\0invalid");
        var runtime = new RuntimeUpdateDownload(discovery);
        await Assert.ThrowsAsync<UpdateDownloadException>(() => runtime.PrepareAsync(null!, Preview,
            new Progress<UpdateDownloadProgress>(), CancellationToken.None));
    }

    [Fact]
    public async Task DownloadRequiresAnExplicitClickAndAuthenticatedDiscoverySnapshot()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        var calls = 0;
        var store = new MemoryHistory();
        var clock = fixture.Clock;
        await using var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            clock, store, downloads: new((value, channel, _, _) =>
            {
                Assert.Same(candidate, value);
                Assert.Equal(Preview, channel);
                Assert.True(store.Value.RequestPending);
                Assert.Equal(clock.Now, store.Value.LastAttemptUtc);
                calls++;
                return Task.CompletedTask;
            }, Discard));
        await session.DownloadAsync(Preview);
        Assert.Equal(0, calls);
        await session.CheckAsync(Preview);
        Assert.Equal(0, calls);
        var checkedAt = store.Value.PreviewSuccessUtc;
        clock.Now += TimeSpan.FromHours(1);
        await session.DownloadAsync(Preview);
        Assert.Equal(1, calls);
        Assert.Equal(checkedAt, store.Value.PreviewSuccessUtc); // Download is not a fresh release check.
        Assert.False(store.Value.RequestPending);
        Assert.True(session.Status.CanDiscardDownload);
        Assert.False(session.Status.CanCheck);
        Assert.Contains("Nothing has been installed", session.Status.DownloadText);
        await session.DiscardDownloadAsync();
        Assert.True(session.Status.CanCheck);
        Assert.Contains("discarded", session.Status.DownloadText);
    }

    [Fact]
    public async Task DiscoveryDownloadAndRepeatedClicksShareOneFlightAndCancellationDrains()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        var calls = 0;
        var cleaned = false;
        var store = new MemoryHistory();
        await using var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            fixture.Clock, store, downloads: new(async (_, _, _, token) =>
            {
                calls++;
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { cleaned = true; }
            }, Discard));
        await session.CheckAsync(Preview);
        var first = session.DownloadAsync(Preview);
        Assert.Same(first, session.DownloadAsync(Preview));
        Assert.Same(first, session.CheckAsync(UpdateReleaseChannel.Stable));
        Assert.True(session.Status.CanCancelDownload);
        session.CancelDownload();
        await first;
        Assert.Equal(1, calls);
        Assert.True(cleaned);
        Assert.False(store.Value.RequestPending);
        Assert.False(session.Status.CanDiscardDownload);
        Assert.True(session.Status.CanCheck);
        Assert.Contains("cancelled", session.Status.DownloadText);
    }

    [Theory]
    [InlineData(25, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public async Task StaleFutureOrDifferentSavedChannelRequiresAnotherCheck(int hours, bool switchChannel)
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        await using var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            fixture.Clock, downloads: new((_, _, _, _) => throw new Exception("Must not download"), Discard));
        await session.CheckAsync(Preview);
        fixture.Clock.Now += TimeSpan.FromHours(hours);
        await session.DownloadAsync(switchChannel ? UpdateReleaseChannel.Stable : Preview);
        Assert.False(session.Status.HasCandidate);
        Assert.Contains("incomplete", session.Status.StatusText);
    }

    [Fact]
    public async Task DownloadRetryPersistsAcrossCancellationRestartAndChannelChange()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        var store = new MemoryHistory();
        var retry = fixture.Clock.Now.AddDays(2);
        UpdateCheckSession? session = null;
        session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            fixture.Clock, store, downloads: new((_, _, _, _) =>
            {
                session!.CancelDownload();
                throw new UpdateDownloadException("secret url must never be shown", retryAt: retry);
            }, Discard));
        await using (session)
        {
            await session.CheckAsync(Preview);
            await session.DownloadAsync(Preview);
            Assert.Equal(retry, store.Value.RetryAfterUtc);
            Assert.False(store.Value.RequestPending);
            Assert.DoesNotContain("secret", session.Status.DownloadText);
        }
        await using var restarted = new UpdateCheckSession((_, _) => throw new Exception("No HTTP during retry"), fixture.Clock, store);
        await restarted.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.Contains("asked us to wait", restarted.Status.StatusText);
    }

    [Fact]
    public async Task LostDownloadOutcomeLeavesPendingEvidenceAndBlocksAnotherRequest()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        var store = new MemoryHistory { FailWriteAt = 4 };
        await using (var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            fixture.Clock, store, downloads: new((_, _, _, _) => Task.CompletedTask, Discard)))
        {
            await session.CheckAsync(Preview);
            await session.DownloadAsync(Preview);
            Assert.True(store.Value.RequestPending);
            Assert.False(session.Status.CanDownload);
            Assert.False(session.Status.CanDiscardDownload);
            Assert.Contains("history is unavailable", session.Status.StatusText);
        }
        await using var restarted = new UpdateCheckSession((_, _) => throw new Exception("No request"), fixture.Clock, store);
        await restarted.CheckAsync(Preview);
        Assert.Contains("history is unavailable", restarted.Status.StatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteCleanupNeverAllowsASecondDownload(bool cancellation)
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        UpdateCheckSession? session = null;
        session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            fixture.Clock, downloads: new((_, _, _, token) =>
            {
                if (!cancellation) throw new UpdateDownloadException("private path", cleanupIncomplete: true);
                session!.CancelDownload();
                var error = new OperationCanceledException(token);
                error.Data["UpdateCleanupIncomplete"] = true;
                throw error;
            }, Discard));
        await using (session)
        {
            await session.CheckAsync(Preview);
            await session.DownloadAsync(Preview);
            Assert.False(session.Status.CanDownload);
            Assert.Contains("could not be removed", session.Status.DownloadText);
            Assert.DoesNotContain("private path", session.Status.DownloadText);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardReportsFailureWithoutClaimingRemoval(bool throws)
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        await using var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            fixture.Clock, downloads: new((_, _, _, _) => Task.CompletedTask,
                _ => throws ? throw new IOException("secret path") : Task.FromResult(false)));
        await session.CheckAsync(Preview);
        await session.DownloadAsync(Preview);
        await session.DiscardDownloadAsync();
        Assert.True(session.Status.CanDiscardDownload);
        Assert.False(session.Status.CanCheck);
        Assert.Contains("remains on disk", session.Status.DownloadText);
        Assert.DoesNotContain("secret", session.Status.DownloadText);
    }

    [Fact]
    public async Task SuspendCancelsAndDrainsTheDownloadBeforeReturningAndResumeNeverRestartsIt()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var candidate = await Available(fixture);
        var completed = false;
        await using var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, candidate)),
            fixture.Clock, downloads: new(async (_, _, _, token) =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { completed = true; }
            }, Discard));
        await session.CheckAsync(Preview);
        var download = session.DownloadAsync(Preview);
        await session.SuspendAsync();
        Assert.True(download.IsCompleted);
        Assert.True(completed);
        Assert.False(session.Status.CanCheck);
        session.Resume();
        Assert.True(session.Status.CanCheck);
        Assert.False(session.Status.IsDownloading);
    }

    [Fact]
    public void ProgressNeverClaimsReadinessAndIgnoresInvalidOrLateValues()
    {
        var status = new PrivacyMask.App.ViewModels.UpdateStatusViewModel();
        status.BeginDownload();
        status.DownloadProgress(new(UpdateDownloadPhase.Ready, long.MaxValue, 1));
        Assert.Equal(100, status.DownloadPercent);
        Assert.False(status.CanDiscardDownload);
        Assert.Contains("Finishing", status.DownloadText);
        status.DownloadProgress(new(UpdateDownloadPhase.Downloading, -1, 1));
        Assert.Equal(100, status.DownloadPercent);
        status.FinishDownload("Cancelled");
        status.DownloadProgress(new(UpdateDownloadPhase.Downloading, 1, 1));
        Assert.Equal("Cancelled", status.DownloadText);
    }

    private static async Task<UpdateCandidate> Available(UpdateDiscoveryFixture fixture)
    {
        fixture.Handler.Releases.Add(fixture.Release());
        using var client = fixture.Client();
        return (await client.CheckAsync(fixture.Context)).Candidate!;
    }

    private sealed class MemoryHistory : IUpdateCheckHistoryStore
    {
        internal UpdateCheckHistory Value = new();
        internal int FailWriteAt;
        private int _writes;
        public IDisposable Acquire() => new Lease();
        private sealed class Lease : IDisposable { public void Dispose() { } }
        public Task<UpdateCheckHistory> ReadAsync(CancellationToken token) => Task.FromResult(Value);
        public Task WriteAsync(UpdateCheckHistory history, CancellationToken token)
        {
            if (++_writes == FailWriteAt) throw new IOException("private path");
            Value = history;
            return Task.CompletedTask;
        }
    }
}
