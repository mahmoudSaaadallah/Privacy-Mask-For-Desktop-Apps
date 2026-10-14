using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateDiscoveryTests
{
    [Fact]
    public async Task AuthenticatesMetadataAndPinsIdsWithoutDownloadingOrExecutingTheZip()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Releases.Add(fixture.Release());
        using var client = fixture.Client();
        var result = await client.CheckAsync(fixture.Context);
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal("2.0.0-beta.3", result.Candidate!.Manifest.Version);
        Assert.Equal(10, result.Candidate.ReleaseId);
        Assert.Equal(103, result.Candidate.PackageAssetId);
        Assert.Equal(GitHubUrls.AssetApi(103), result.Candidate.PackageApiUrl);
        Assert.Equal("<script>display as text, never execute</script>", result.Candidate.ReleaseNotes);
        Assert.Equal(3, fixture.Handler.Requests.Count);
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Uri == GitHubUrls.AssetApi(103));
        Assert.All(fixture.Handler.Requests, request => Assert.False(request.HasCredentials));
        Assert.Equal("application/vnd.github+json", fixture.Handler.Requests[0].Accept);
        Assert.Equal("application/octet-stream", fixture.Handler.Requests[1].Accept);
    }

    [Fact]
    public async Task SelectsHighestSemanticVersionNotListOrderOrPublishDate()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Releases.Add(fixture.Release("2.0.0-beta.3", 10));
        fixture.Handler.Releases.Add(fixture.Release("2.0.0-beta.10", 11));
        fixture.Handler.Releases.Add(fixture.Release("2.0.0-beta.9", 12));
        using var client = fixture.Client();
        Assert.Equal("2.0.0-beta.10", (await client.CheckAsync(fixture.Context)).Candidate!.Manifest.Version);
    }

    [Fact]
    public async Task StableChannelIgnoresPreviewWithoutFetchingItsAssets()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Releases.Add(fixture.Release("2.0.0-beta.10", 10));
        fixture.Handler.Releases.Add(fixture.Release("2.0.0", 11));
        using var client = fixture.Client();
        var result = await client.CheckAsync(fixture.Context with { Channel = UpdateChannel.Stable });
        Assert.Equal("2.0.0", result.Candidate!.Manifest.Version);
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Uri == GitHubUrls.AssetApi(101));
    }

    [Fact]
    public async Task NoKeysAndUnsupportedInstallationsMakeNoRequests()
    {
        using var fixture = new UpdateDiscoveryFixture();
        using var client = new GitHubReleaseClient(new([]), fixture.Clock, fixture.Handler);
        Assert.Equal(UpdateCheckStatus.NotConfigured, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Equal(UpdateCheckStatus.ManualUpdateRequired,
            (await client.CheckAsync(fixture.Context with { Installation = InstallationKind.Source })).Status);
        Assert.Equal(UpdateCheckStatus.InvalidContext,
            (await client.CheckAsync(fixture.Context with { InstalledVersion = "not-a-version" })).Status);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("equal")]
    [InlineData("older")]
    public async Task DraftsEqualAndOlderVersionsAreNotUpdates(string kind)
    {
        using var fixture = new UpdateDiscoveryFixture();
        var release = fixture.Release(kind == "equal" ? "2.0.0-beta.2" : kind == "older" ? "1.9.0" : "2.0.0");
        if (kind == "draft") release["draft"] = true;
        fixture.Handler.Releases.Add(release);
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.UpToDate, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-name")]
    [InlineData("duplicate-id")]
    [InlineData("offsite")]
    [InlineData("state")]
    [InlineData("size")]
    [InlineData("digest")]
    public async Task BadNewerAssetsAreRejectedNotReportedAsUpToDate(string fault)
    {
        using var fixture = new UpdateDiscoveryFixture();
        var release = fixture.Release();
        var assets = release["assets"]!.AsArray();
        switch (fault)
        {
            case "missing": assets.RemoveAt(0); break;
            case "duplicate-name": assets[1]!["name"] = "UPDATE-MANIFEST.JSON"; break;
            case "duplicate-id": assets[1]!["id"] = 101; break;
            case "offsite": assets[0]!["browser_download_url"] = "https://example.invalid/metadata"; break;
            case "state": assets[0]!["state"] = "new"; break;
            case "size": assets[0]!["size"] = 65537; break;
            case "digest": assets[0]!["digest"] = "sha256:wrong"; break;
        }
        fixture.Handler.Releases.Add(release);
        using var client = fixture.Client();
        var result = await client.CheckAsync(fixture.Context);
        Assert.Equal(UpdateCheckStatus.UnableToCheck, result.Status);
        Assert.Null(result.Candidate);
        Assert.Equal(1, result.RejectedReleases);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task InconsistentGitHubPrereleaseFlagCannotHideAKnownNewerVersion()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var release = fixture.Release();
        release["prerelease"] = false;
        fixture.Handler.Releases.Add(release);
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.UnableToCheck, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task AlteredSignatureCannotAuthorizeUpdateEvenWhenListingDigestIsMissing()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var release = fixture.Release();
        fixture.Handler.Assets[102] = Encoding.UTF8.GetBytes("{\"algorithm\":\"none\",\"keyId\":\"test-key\",\"signature\":\"AA==\"}");
        release["assets"]![1]!["digest"] = null;
        release["assets"]![1]!["size"] = fixture.Handler.Assets[102].Length;
        fixture.Handler.Releases.Add(release);
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.UnableToCheck, (await client.CheckAsync(fixture.Context)).Status);
    }

    [Theory]
    [InlineData("os")]
    [InlineData("protocol")]
    [InlineData("settings")]
    public async Task AuthenticatedButIncompatibleReleasesAreDistinctFromUpToDate(string fault)
    {
        using var fixture = new UpdateDiscoveryFixture();
        var release = fixture.Release(changeManifest: manifest => fault switch
        {
            "os" => manifest with { MinWindowsBuild = 99999 },
            "protocol" => manifest with { MinUpdaterProtocol = 2 },
            _ => manifest with { SettingsCompatibility = new(13, 13, 13) }
        });
        fixture.Handler.Releases.Add(release);
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.NoCompatibleUpdate, (await client.CheckAsync(fixture.Context)).Status);
    }

    [Fact]
    public async Task CanChooseNextAuthenticCompatibleReleaseAfterRejectingTheNewest()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var invalid = fixture.Release("2.0.0-beta.10", 10);
        invalid["assets"]!.AsArray().RemoveAt(0);
        fixture.Handler.Releases.Add(invalid);
        fixture.Handler.Releases.Add(fixture.Release("2.0.0-beta.3", 11));
        using var client = fixture.Client();
        var result = await client.CheckAsync(fixture.Context);
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal(1, result.RejectedReleases);
        Assert.Equal("2.0.0-beta.3", result.Candidate!.Manifest.Version);
    }

    [Fact]
    public async Task SingleFlightAndCancellationLeaveClientUsable()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Intercept = async (_, token) =>
        {
            entered.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, token);
            return FakeUpdateHandler.Json("[]");
        };
        using var client = fixture.Client();
        using var cancel = new CancellationTokenSource();
        var first = client.CheckAsync(fixture.Context, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(UpdateCheckStatus.InProgress, (await client.CheckAsync(fixture.Context)).Status);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        fixture.Handler.Intercept = null;
        Assert.Equal(UpdateCheckStatus.UpToDate, (await client.CheckAsync(fixture.Context)).Status);
    }
}
