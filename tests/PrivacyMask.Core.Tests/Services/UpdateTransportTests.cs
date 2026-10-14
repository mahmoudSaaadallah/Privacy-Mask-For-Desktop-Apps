using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateTransportTests
{
    [Fact]
    public async Task UsesBoundedEtagCacheOnlyAfterServerRevalidation()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Releases.Add(fixture.Release());
        fixture.Handler.Intercept = (request, _) =>
        {
            var response = request.RequestUri == GitHubUrls.ListPage(1) && request.Headers.IfNoneMatch.Count != 0
                ? new HttpResponseMessage(HttpStatusCode.NotModified) : fixture.Handler.Default(request);
            if (request.RequestUri == GitHubUrls.ListPage(1)) response.Headers.ETag = new("\"page-1\"");
            return Task.FromResult(response);
        };
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.Available, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Equal(UpdateCheckStatus.Available, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Equal("\"page-1\"", fixture.Handler.Requests[3].ETag);
        Assert.Equal(6, fixture.Handler.Requests.Count); // signatures are re-fetched, never trusted from stale cache
    }

    [Theory]
    [InlineData(304)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(206)]
    public async Task FailedOrPartialListingsAreNeverUpToDate(int code)
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Intercept = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("[]") });
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.UnableToCheck, (await client.CheckAsync(fixture.Context)).Status);
    }

    [Fact]
    public async Task RateLimitAndResetPreventManualRequestsUntilRetryTime()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Intercept = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(120));
            response.Headers.Add("X-RateLimit-Reset", fixture.Clock.Now.AddMinutes(5).ToUnixTimeSeconds().ToString());
            return Task.FromResult(response);
        };
        using var client = fixture.Client();
        var first = await client.CheckAsync(fixture.Context);
        Assert.Equal(UpdateCheckStatus.RateLimited, first.Status);
        Assert.Equal(fixture.Clock.Now.AddMinutes(5), first.RetryAt);
        Assert.Equal(UpdateCheckStatus.RateLimited, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Single(fixture.Handler.Requests);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        fixture.Handler.Intercept = null;
        Assert.Equal(UpdateCheckStatus.UpToDate, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("https://example.invalid/payload")]
    [InlineData("https://release-assets.githubusercontent.com.evil.invalid/github-production-release-asset/file")]
    [InlineData("http://release-assets.githubusercontent.com/github-production-release-asset/file")]
    [InlineData("https://user:password@release-assets.githubusercontent.com/github-production-release-asset/file")]
    [InlineData("https://release-assets.githubusercontent.com:444/github-production-release-asset/file")]
    [InlineData("https://release-assets.githubusercontent.com/not-a-release/file")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/file#fragment")]
    public async Task RejectsUnsafeAssetRedirectsWithoutFollowingThem(string location)
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Releases.Add(fixture.Release());
        fixture.Handler.Intercept = (request, _) =>
        {
            if (request.RequestUri != GitHubUrls.AssetApi(101)) return Task.FromResult(fixture.Handler.Default(request));
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new(location);
            return Task.FromResult(response);
        };
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.UnableToCheck, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Uri.AbsoluteUri == location);
    }

    [Fact]
    public async Task AllowsOnlyPinnedGitHubDownloadHostsAndNeverReturnsSasQueries()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Releases.Add(fixture.Release());
        var location = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1/file?sig=test-secret");
        fixture.Handler.Intercept = (request, _) =>
        {
            if (request.RequestUri == GitHubUrls.AssetApi(101))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = location;
                return Task.FromResult(redirect);
            }
            return Task.FromResult(request.RequestUri == location
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Handler.Assets[101]) }
                : fixture.Handler.Default(request));
        };
        using var client = fixture.Client();
        var result = await client.CheckAsync(fixture.Context);
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Empty(result.Candidate!.PackageApiUrl.Query);
        Assert.DoesNotContain("test-secret", result.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundsBodiesWithAndWithoutContentLength(bool declared)
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Intercept = (_, _) =>
        {
            var content = declared ? (HttpContent)new ByteArrayContent(new byte[1024 * 1024 + 1])
                : new StreamContent(new MemoryStream(new byte[1024 * 1024 + 1]));
            if (!declared) content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.UnableToCheck, (await client.CheckAsync(fixture.Context)).Status);
    }

    [Fact]
    public async Task DoesNotFollowExternalPaginationLinks()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Intercept = (_, _) =>
        {
            var response = FakeUpdateHandler.Json("[]");
            response.Headers.Add("Link", "<https://example.invalid/page>; rel=\"next\"");
            return Task.FromResult(response);
        };
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.Incomplete, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("rel=next")]
    [InlineData("rel = \"next\"")]
    [InlineData("rel=\"next last\"")]
    public async Task NumericRepositoryLinksOnlyTriggerOurOwnCanonicalNextRequest(string relation)
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Intercept = (request, _) =>
        {
            var response = FakeUpdateHandler.Json("[]");
            if (request.RequestUri == GitHubUrls.ListPage(1))
                response.Headers.TryAddWithoutValidation("Link", $"<https://api.github.com/repositories/123456/releases?page=2&per_page=30>; {relation}");
            return Task.FromResult(response);
        };
        using var client = fixture.Client();
        Assert.Equal(UpdateCheckStatus.UpToDate, (await client.CheckAsync(fixture.Context)).Status);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.Equal(GitHubUrls.ListPage(2), fixture.Handler.Requests[1].Uri);
    }

    [Fact]
    public async Task ChecksMultiplePagesAndDetectsChangingOrAmbiguousListings()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var first = new JsonArray(fixture.Release("2.0.0-beta.3", 10));
        var second = new JsonArray(fixture.Release("2.0.0-beta.10", 11));
        fixture.Handler.Intercept = (request, _) =>
        {
            if (request.RequestUri == GitHubUrls.ListPage(1))
            {
                var response = FakeUpdateHandler.Json(first.ToJsonString());
                response.Headers.Add("Link", $"<{GitHubUrls.ListPage(2)}>; rel=\"next\"");
                return Task.FromResult(response);
            }
            return Task.FromResult(request.RequestUri == GitHubUrls.ListPage(2)
                ? FakeUpdateHandler.Json(second.ToJsonString()) : fixture.Handler.Default(request));
        };
        using var client = fixture.Client();
        Assert.Equal("2.0.0-beta.10", (await client.CheckAsync(fixture.Context)).Candidate!.Manifest.Version);
        second.Clear(); second.Add(first[0]!.DeepClone());
        Assert.Equal(UpdateCheckStatus.Incomplete, (await client.CheckAsync(fixture.Context)).Status);
    }

    [Fact]
    public async Task NetworkExceptionsAndDuplicateJsonReturnSafeFailure()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Handler.Intercept = (_, _) => throw new HttpRequestException("https://private.invalid/?sig=secret");
        using var client = fixture.Client();
        var result = await client.CheckAsync(fixture.Context);
        Assert.Equal(UpdateCheckStatus.UnableToCheck, result.Status);
        Assert.DoesNotContain("secret", result.Reason!);
        fixture.Handler.Intercept = (_, _) => Task.FromResult(FakeUpdateHandler.Json("[{\"id\":1,\"id\":2}]"));
        Assert.Equal(UpdateCheckStatus.UnableToCheck, (await client.CheckAsync(fixture.Context)).Status);
    }
}
