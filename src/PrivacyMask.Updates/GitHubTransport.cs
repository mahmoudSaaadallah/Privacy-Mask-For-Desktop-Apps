using System.Net;
using System.Net.Http.Headers;
using System.Globalization;

namespace PrivacyMask.Updates;

internal sealed class UpdateHttpException(UpdateCheckStatus status, string code) : Exception(code)
{
    public UpdateCheckStatus Status { get; } = status;
}

internal sealed record ReleasePageResponse(byte[] Bytes, string? Link);

internal sealed class GitHubTransport : IDisposable
{
    internal const int PageLimit = 1024 * 1024;
    private sealed record CachedPage(byte[] Bytes, EntityTagHeaderValue Tag, string? Link);
    private readonly Dictionary<int, CachedPage> _cache = [];
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    public DateTimeOffset? RetryAt { get; private set; }

    public GitHubTransport(TimeProvider time, HttpMessageHandler? handler)
    {
        _time = time;
        _http = new(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(10), MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PrivacyMask-Updates/1.0");
    }

    private HttpRequestMessage Request(Uri uri, bool binary)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new(binary ? "application/octet-stream" : "application/vnd.github+json"));
        if (uri.Host == "api.github.com") request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    public async Task<ReleasePageResponse> GetPageAsync(int page, CancellationToken cancellationToken)
    {
        using var request = Request(GitHubUrls.ListPage(page), false);
        _cache.TryGetValue(page, out var previous);
        if (previous is not null) request.Headers.IfNoneMatch.Add(previous.Tag);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return previous is null ? throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, "cache-miss-on-304")
                : new(previous.Bytes, previous.Link);
        EnsureSuccess(response);
        var bytes = await ReadBoundedAsync(response.Content, PageLimit, cancellationToken).ConfigureAwait(false);
        var link = response.Headers.TryGetValues("Link", out var links) ? string.Join(',', links) : null;
        if (link?.Length > 4096) throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, "oversized-pagination-header");
        if (page == 1 && (previous is null || response.Headers.ETag?.ToString() != previous.Tag.ToString()))
            foreach (var key in _cache.Keys.Where(key => key > 1).ToArray()) _cache.Remove(key);
        var tag = response.Headers.ETag;
        if (tag is not null && tag.ToString().Length <= 256) _cache[page] = new(bytes, tag, link);
        else _cache.Remove(page);
        return new(bytes, link);
    }

    public async Task<byte[]> GetAssetAsync(long id, Uri browserAsset, int limit, CancellationToken cancellationToken)
    {
        var api = GitHubUrls.AssetApi(id);
        var current = api;
        var visited = new HashSet<Uri>();
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            if (!GitHubUrls.IsAllowedAssetTarget(current, api, browserAsset) || !visited.Add(current))
                throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, "unsafe-asset-redirect");
            using var request = Request(current, true);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                if (location is null) throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, "missing-redirect-location");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            EnsureSuccess(response);
            return await ReadBoundedAsync(response.Content, limit, cancellationToken).ConfigureAwait(false);
        }
        throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, "too-many-redirects");
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.OK) return;
        if ((int)response.StatusCode is 403 or 429 || response.Headers.RetryAfter is not null)
        {
            var now = _time.GetUtcNow();
            var retry = now.AddMinutes(1);
            var header = response.Headers.RetryAfter;
            if (header?.Date is { } date && date > retry) retry = date;
            if (header?.Delta is { } delta && delta > TimeSpan.Zero) retry = Max(retry, now + (delta > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delta));
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
                long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var reset) &&
                reset is >= 0 and <= 253402300799) retry = Max(retry, DateTimeOffset.FromUnixTimeSeconds(reset));
            RetryAt = retry > now.AddDays(1) ? now.AddDays(1) : retry;
            throw new UpdateHttpException(UpdateCheckStatus.RateLimited, "server-retry-required");
        }
        // Never treat missing releases/offline as an up-to-date response.
        throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, $"http-{(int)response.StatusCode}");
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length && (length < 0 || length > limit))
            throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, "oversized-http-body");
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > limit) throw new UpdateHttpException(UpdateCheckStatus.UnableToCheck, "oversized-http-body");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
