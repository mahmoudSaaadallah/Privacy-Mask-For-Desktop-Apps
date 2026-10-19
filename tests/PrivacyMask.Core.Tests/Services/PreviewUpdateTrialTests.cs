using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;
using System.Text.Json;

namespace PrivacyMask.Core.Tests.Services;

public sealed class PreviewUpdateTrialTests
{
    [Theory]
    [InlineData("2.0.0-beta.14", 10)]
    [InlineData("2.0.0-beta.14+abc123", 10)]
    [InlineData("2.0.0-beta.13", 1440)]
    [InlineData("2.0.0-beta.15", 1440)]
    [InlineData("2.0.0-beta.15+commit", 1440)]
    [InlineData("2.0.0-beta.140", 1440)]
    [InlineData("2.0.0-rc.1", 1440)]
    [InlineData("2.0.0", 1440)]
    [InlineData("invalid", 1440)]
    public void OnlyBootstrapAHasTheShortCadence(string version, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), UpdateRuntimePolicy.GetSuccessfulCheckInterval(version));

    [Theory]
    [InlineData("2.0.0-beta.14", true)]
    [InlineData("2.0.0-beta.15+commit", true)]
    [InlineData("2.0.0", false)]
    [InlineData("2.0.1", false)]
    [InlineData("unknown", false)]
    public void PreviewExperimentCannotSilentlyActivateAStableBinary(string version, bool expected) =>
        Assert.Equal(expected, UpdateRuntimePolicy.IsPreviewRuntimeVersion(version));

    [Theory]
    [InlineData("2.0.0-beta.14", 21999, false)]
    [InlineData("2.0.0-beta.14", 22000, true)]
    [InlineData("2.0.0-beta.14", 26100, true)]
    [InlineData("2.0.0-beta.15", 26200, true)]
    [InlineData("2.0.0", 26100, false)]
    [InlineData("unknown", 26100, false)]
    public void RuntimePreviewFilterHasAnExplicitWindowsFloor(string version, int build, bool expected) =>
        Assert.Equal(expected, UpdateRuntimePolicy.SupportsAutomaticUpdates(version, build));

    [Fact]
    public void RestartingBReusesHistoryButReturnsToDailyWithoutChangingItsSchema()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new UpdateCheckHistory(LastAttemptUtc: now, PreviewSuccessUtc: now);
        var json = new UpdateCheckHistoryJsonContext(new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 4 });
        var original = JsonSerializer.SerializeToUtf8Bytes(history, json.UpdateCheckHistory);
        history = JsonSerializer.Deserialize(original, json.UpdateCheckHistory)!;
        Assert.Equal(now.AddMinutes(10), history.MandatoryDueAt(now,
            UpdateRuntimePolicy.GetSuccessfulCheckInterval("2.0.0-beta.14")));
        Assert.Equal(now.AddHours(24), history.MandatoryDueAt(now,
            UpdateRuntimePolicy.GetSuccessfulCheckInterval("2.0.0-beta.15")));
        Assert.Equal(original, JsonSerializer.SerializeToUtf8Bytes(history, json.UpdateCheckHistory));
        Assert.Equal(1, history.SchemaVersion);
    }

    [Fact]
    public void TenMinuteTrialNeverShortensFailuresServerDeadlinesOrUnknownAdmissions()
    {
        var now = DateTimeOffset.UtcNow;
        var trial = TimeSpan.FromMinutes(10);
        var failure = new UpdateCheckHistory(LastAttemptUtc: now);
        Assert.Equal(now.AddMinutes(15), failure.MandatoryDueAt(now, trial));
        var successful = failure with { PreviewSuccessUtc = now, RetryAfterUtc = now.AddHours(2) };
        Assert.Equal(now.AddHours(2), successful.MandatoryDueAt(now, trial));
        Assert.Equal(DateTimeOffset.MaxValue, (successful with { RequestPending = true }).MandatoryDueAt(now, trial));
    }

    [Fact]
    public void InvalidCadencesAreNotAcceptedByHistory()
    {
        var history = new UpdateCheckHistory();
        foreach (var interval in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromHours(25) })
            Assert.Throws<ArgumentOutOfRangeException>(() => history.MandatoryDueAt(DateTimeOffset.UtcNow, interval));
    }

    [Fact]
    public async Task TenMinuteClockDiscoversSignedBWithoutAUserCheck()
    {
        using var fixture = new UpdateDiscoveryFixture();
        using var client = fixture.Client();
        var context = fixture.Context with { InstalledVersion = "2.0.0-beta.14" };
        var store = new History();
        await using var session = new UpdateCheckSession((_, token) => client.CheckAsync(context, token),
            fixture.Clock, store, mandatory: true,
            successfulCheckInterval: UpdateRuntimePolicy.GetSuccessfulCheckInterval("2.0.0-beta.14"));
        var downloads = 0;
        var installed = false;
        var controller = new MandatoryUpdateController(session.Status, new(() => true, () => true,
            _ => session.CheckMandatoryAsync(() => new(false, UpdateReleaseChannel.Preview)),
            () => { downloads++; session.Status.FinishDownload("fixture only", ready: true); return Task.CompletedTask; },
            () => Task.CompletedTask, () => { installed = true; return Task.CompletedTask; },
            () => session.NextAutomaticAttemptUtc, () => null), fixture.Clock);
        await controller.PulseAsync();
        Assert.Single(fixture.Handler.Requests);
        fixture.Handler.Releases.Add(fixture.Release("2.0.0-beta.15"));
        fixture.Clock.Now += TimeSpan.FromMinutes(9);
        await controller.PulseAsync();
        Assert.Single(fixture.Handler.Requests);
        fixture.Clock.Now += TimeSpan.FromMinutes(1);
        await controller.PulseAsync();
        Assert.Equal(1, downloads);
        Assert.True(installed);
        Assert.Equal("2.0.0-beta.15", session.Status.Candidate?.Manifest.Version);
        // Only signed discovery is real here. Download/apply are synthetic state
        // transitions, not evidence of an installed-App update on a real device.
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
