using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class DurableUpdateRequestTests
{
    [Fact]
    public async Task NativeApplyFactoryWithoutTrustDoesNotInspectInvalidRoot()
    {
        using var client = GitHubReleaseClient.ForManagedApply(new([]), "\0invalid");
        var result = await client.CheckAsync(new("2.0.0-beta.10", UpdateChannel.Preview, "win-x64", 22631, 1, 13, InstallationKind.Managed));
        Assert.Equal(UpdateCheckStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task SharedGlobalRetryBlocksAnotherApplyProcessOrChannel()
    {
        var clock = new DiscoveryClock();
        var store = new Store();
        var retry = clock.Now.AddDays(1);
        await DurableUpdateRequest.RunAsync(store, UpdateChannel.Preview, _ =>
        {
            Assert.True(store.Value.RequestPending);
            Assert.True(store.Held);
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.RateLimited, RetryAt: retry));
        }, clock, CancellationToken.None);
        var result = await DurableUpdateRequest.RunAsync(store, UpdateChannel.Stable, _ => throw new Exception("No HTTP"), clock, CancellationToken.None);
        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
        Assert.Equal(retry, result.RetryAt);
        Assert.False(store.Value.RequestPending);
        Assert.False(store.Held);
    }

    [Fact]
    public async Task LostCompletionFailsClosedAndNeverRetriesAnUnknownOutcome()
    {
        var store = new Store { FailWriteAt = 2 };
        var clock = new DiscoveryClock();
        var result = await DurableUpdateRequest.RunAsync(store, UpdateChannel.Preview,
            _ => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate)), clock, CancellationToken.None);
        Assert.Equal(UpdateCheckStatus.UnableToCheck, result.Status);
        Assert.True(store.Value.RequestPending);
        await DurableUpdateRequest.RunAsync(store, UpdateChannel.Stable, _ => throw new Exception("No HTTP"), clock, CancellationToken.None);
        Assert.True(store.Value.RequestPending);
    }

    [Fact]
    public async Task CancellationRacingReceivedRetryStillFinalizesUnderTheLease()
    {
        var store = new Store();
        var clock = new DiscoveryClock();
        using var cancel = new CancellationTokenSource();
        var result = await DurableUpdateRequest.RunAsync(store, UpdateChannel.Preview, _ =>
        {
            cancel.Cancel();
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.RateLimited, RetryAt: clock.Now.AddHours(1)));
        }, clock, cancel.Token);
        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
        Assert.False(store.Value.RequestPending);
        Assert.Equal(clock.Now.AddHours(1), store.Value.RetryAfterUtc);
    }

    [Fact]
    public async Task CancellationCleanupHoldsTheSameLeaseAndDoesNotAdvanceSuccess()
    {
        var store = new Store();
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DurableUpdateRequest.RunAsync(store, UpdateChannel.Preview, token =>
        {
            cancel.Cancel();
            token.ThrowIfCancellationRequested();
            throw new Exception("No return");
        }, new DiscoveryClock(), cancel.Token));
        Assert.False(store.Value.RequestPending);
        Assert.Null(store.Value.PreviewSuccessUtc);
        Assert.False(store.Held);
    }

    private sealed class Store : IUpdateCheckHistoryStore
    {
        internal UpdateCheckHistory Value = new();
        internal bool Held;
        internal int FailWriteAt;
        private int _writes;
        public IDisposable Acquire() { Assert.False(Held); Held = true; return new Lease(this); }
        private sealed class Lease(Store store) : IDisposable { public void Dispose() => store.Held = false; }
        public Task<UpdateCheckHistory> ReadAsync(CancellationToken token) { Assert.True(Held); return Task.FromResult(Value); }
        public Task WriteAsync(UpdateCheckHistory history, CancellationToken token)
        {
            Assert.True(Held);
            if (++_writes == FailWriteAt) throw new IOException("private path");
            Value = history;
            return Task.CompletedTask;
        }
    }
}
