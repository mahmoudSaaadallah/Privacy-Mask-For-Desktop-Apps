using System.IO;
using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateCheckSessionTests
{
    [Fact]
    public async Task UnsupportedRuntimeMakesNoNetworkAndDoesNotInspectInvalidSettingsPath()
    {
        using var discovery = new RuntimeUpdateDiscovery("\0invalid");
        var result = await discovery.CheckAsync(UpdateReleaseChannel.Preview, CancellationToken.None);
        Assert.Equal(UpdateCheckStatus.ManualUpdateRequired, result.Status);
    }

    [Fact]
    public async Task ManualChecksAreSingleFlightAndKeepTheSavedChannel()
    {
        var calls = 0;
        var response = new TaskCompletionSource<UpdateCheckResult>();
        await using var session = new UpdateCheckSession((channel, token) =>
        {
            Assert.Equal(UpdateReleaseChannel.Stable, channel);
            calls++;
            return response.Task.WaitAsync(token);
        });
        var first = session.CheckAsync(UpdateReleaseChannel.Stable);
        var second = session.CheckAsync(UpdateReleaseChannel.Preview);
        Assert.Same(first, second);
        Assert.True(session.Status.IsChecking);
        Assert.False(session.Status.CanCheck);
        Assert.Equal(1, calls);
        response.SetResult(new(UpdateCheckStatus.UpToDate));
        await first;
        Assert.True(session.Status.CanCheck);
        Assert.Contains("Stable", session.Status.LastCheckText);
        Assert.Contains("up to date", session.Status.StatusText);
    }

    [Fact]
    public async Task StopCancelsActiveWorkAndPreventsLateStatusAndNewRequests()
    {
        var entered = new TaskCompletionSource();
        await using var session = new UpdateCheckSession(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(UpdateCheckStatus.UpToDate);
        });
        var pending = session.CheckAsync(UpdateReleaseChannel.Stable);
        await entered.Task;
        session.Stop();
        await pending;
        await session.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.False(session.Status.CanCheck);
        Assert.DoesNotContain("up to date", session.Status.StatusText);
    }

    [Fact]
    public async Task UnexpectedErrorsNeverExposeRawDetailsOrClaimSuccess()
    {
        await using var session = new UpdateCheckSession((_, _) => throw new IOException("secret-token C:\\private\\user"));
        await session.CheckAsync(UpdateReleaseChannel.Preview);
        Assert.Contains("Unable to check", session.Status.StatusText);
        Assert.DoesNotContain("secret", session.Status.StatusText);
        Assert.Contains("never", session.Status.LastCheckText);
    }

    [Theory]
    [InlineData(UpdateCheckStatus.RateLimited)]
    [InlineData(UpdateCheckStatus.Incomplete)]
    [InlineData(UpdateCheckStatus.NotConfigured)]
    [InlineData(UpdateCheckStatus.ManualUpdateRequired)]
    [InlineData(UpdateCheckStatus.InvalidContext)]
    [InlineData(UpdateCheckStatus.UnableToCheck)]
    public async Task FailureStatesNeverClaimUpToDateOrASuccessfulCheck(UpdateCheckStatus result)
    {
        await using var session = new UpdateCheckSession((_, _) => Task.FromResult(new UpdateCheckResult(result)));
        await session.CheckAsync(UpdateReleaseChannel.Stable);
        Assert.DoesNotContain("up to date", session.Status.StatusText);
        Assert.Contains("never", session.Status.LastCheckText);
    }
}
