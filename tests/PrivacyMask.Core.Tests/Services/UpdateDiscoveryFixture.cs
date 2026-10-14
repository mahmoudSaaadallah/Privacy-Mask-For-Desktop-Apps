using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.ReleaseTool;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

internal sealed class UpdateDiscoveryFixture : IDisposable
{
    public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public FakeUpdateHandler Handler { get; } = new();
    public DiscoveryClock Clock { get; } = new();
    public UpdateManifestVerifier Verifier => new([new("test-key", Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo()))]);
    public UpdateCheckContext Context { get; } = new("2.0.0-beta.2+installed-source", UpdateChannel.Preview,
        "win-x64", 22631, 1, 12, InstallationKind.Managed);
    public GitHubReleaseClient Client() => new(Verifier, Clock, Handler);

    public JsonObject Release(string version = "2.0.0-beta.3", long id = 10,
        Func<UpdateManifest, UpdateManifest>? changeManifest = null)
    {
        var manifest = new UpdateManifest(1, ManifestRules.AppId, ManifestRules.Repository, version,
            "v" + version, new string('1', 40), version.Contains('-') ? "preview" : "stable", "win-x64",
            19041, 1, new(ManifestRules.ArchiveName(version), 2048, new string('a', 64)),
            [new("PrivacyMask.App.exe", 4096, new string('b', 64))], new(1, 12, 12));
        if (changeManifest is not null) manifest = changeManifest(manifest);
        var bytes = ContractJson.Write(manifest);
        var policy = new ReleasePolicy(1, 19041, 1, new(1, 12, 12),
            [new("test-key", Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo()))]);
        var envelope = ContractJson.Write(ManifestSigning.Sign(bytes, Key.ExportPkcs8PrivateKeyPem(), "test-key", policy));
        var assets = new JsonArray();
        foreach (var pair in new[] { ("update-manifest.json", bytes), ("update-manifest.sig.json", envelope) })
        {
            var assetId = id * 10 + assets.Count + 1;
            Handler.Assets[assetId] = pair.Item2;
            assets.Add(Asset(assetId, pair.Item1, pair.Item2.Length, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(pair.Item2)), "v" + version));
        }
        assets.Add(Asset(id * 10 + 3, ManifestRules.ArchiveName(version), manifest.Package.Size,
            "sha256:" + manifest.Package.Sha256, "v" + version));
        return new JsonObject
        {
            ["id"] = id, ["tag_name"] = "v" + version, ["draft"] = false,
            ["prerelease"] = version.Contains('-'), ["published_at"] = "2026-10-15T00:00:00Z",
            ["body"] = "<script>display as text, never execute</script>", ["assets"] = assets,
            ["unknown_future_github_field"] = "ignored safely"
        };
    }

    private static JsonObject Asset(long id, string name, long size, string digest, string tag) => new()
    {
        ["id"] = id, ["name"] = name, ["size"] = size, ["state"] = "uploaded", ["digest"] = digest,
        ["browser_download_url"] = GitHubUrls.BrowserAsset(tag, name).AbsoluteUri
    };
    public void Dispose() { Key.Dispose(); Handler.Dispose(); }
}

internal sealed class DiscoveryClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed record UpdateRequest(Uri Uri, string Accept, string? ETag, bool HasCredentials);

internal sealed class FakeUpdateHandler : HttpMessageHandler
{
    public Dictionary<long, byte[]> Assets { get; } = [];
    public List<UpdateRequest> Requests { get; } = [];
    public JsonArray Releases { get; set; } = [];
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Intercept { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new(request.RequestUri!, string.Join(',', request.Headers.Accept), request.Headers.IfNoneMatch.FirstOrDefault()?.ToString(),
            request.Headers.Authorization is not null || request.Headers.Contains("Cookie")));
        return Intercept is null ? Task.FromResult(Default(request)) : Intercept(request, cancellationToken);
    }

    public HttpResponseMessage Default(HttpRequestMessage request)
    {
        if (request.RequestUri == GitHubUrls.ListPage(1)) return Json(Releases.ToJsonString());
        if (long.TryParse(request.RequestUri!.Segments.Last(), out var id) && Assets.TryGetValue(id, out var bytes))
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        return new(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
}
