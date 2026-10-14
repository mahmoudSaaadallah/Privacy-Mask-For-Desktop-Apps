using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrivacyMask.Updates;

/// <summary>Read-only discovery. Does not download ZIPs, write settings, or install.</summary>
public sealed class GitHubReleaseClient : IDisposable
{
    private sealed record Asset(long Id, string Name, long Size, string? Digest, Uri BrowserUrl);
    private sealed record Release(long Id, string Tag, SemanticVersion Version, bool Prerelease,
        string Notes, Asset[]? Assets);
    private readonly UpdateManifestVerifier _verifier;
    private readonly GitHubTransport _transport;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Handler injection is for trusted hosts/tests. Default transport disables
    // automatic redirects, cookies and credentials; no HttpClient is shared with UI.
    public GitHubReleaseClient(UpdateManifestVerifier verifier, TimeProvider? timeProvider = null,
        HttpMessageHandler? handler = null)
    {
        _verifier = verifier;
        _time = timeProvider ?? TimeProvider.System;
        _transport = new(_time, handler);
    }

    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var invalid = UpdateEligibility.CheckContext(context);
        if (invalid is not null) return new(invalid.Value);
        if (!_verifier.IsConfigured) return new(UpdateCheckStatus.NotConfigured);
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return new(UpdateCheckStatus.InProgress);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            if (_transport.RetryAt is { } retry && retry > _time.GetUtcNow())
                return new(UpdateCheckStatus.RateLimited, RetryAt: retry, Reason: "server-retry-required");
            return await DiscoverAsync(context, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(UpdateCheckStatus.UnableToCheck, Reason: "timeout"); }
        catch (UpdateHttpException error) { return new(error.Status,
            RetryAt: error.Status == UpdateCheckStatus.RateLimited ? _transport.RetryAt : null, Reason: error.Message); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or
            ReleaseValidationException or FormatException or CryptographicException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            // Never expose HTTP URLs, SAS query values, exception details or local paths.
            return new(UpdateCheckStatus.UnableToCheck, Reason: "invalid-or-unavailable-release-data");
        }
        finally { _gate.Release(); }
    }

    private async Task<UpdateCheckResult> DiscoverAsync(UpdateCheckContext context, CancellationToken token)
    {
        var releases = new List<Release>();
        var ids = new HashSet<long>();
        var tags = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; page <= 5; page++)
        {
            var response = await _transport.GetPageAsync(page, token).ConfigureAwait(false);
            using var json = ContractJson.ReadDocument(response.Bytes, GitHubTransport.PageLimit);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 30)
                throw new ReleaseValidationException("Invalid release listing shape.");
            foreach (var entry in root.EnumerateArray())
            {
                var id = entry.GetProperty("id").GetInt64();
                if (id <= 0 || !ids.Add(id)) return new(UpdateCheckStatus.Incomplete, Reason: "listing-changed-or-ambiguous");
                if (entry.GetProperty("draft").GetBoolean()) continue;
                var tag = entry.GetProperty("tag_name").GetString() ?? "";
                var prerelease = entry.GetProperty("prerelease").GetBoolean();
                if (!tag.StartsWith('v') || !SemanticVersion.TryParse(tag[1..], out var version) ||
                    !UpdateEligibility.IsNewerForChannel(tag[1..], version.IsPrerelease, context)) continue;
                if (!tags.Add(tag)) return new(UpdateCheckStatus.Incomplete, Reason: "ambiguous-release-tag");
                if (version.IsPrerelease != prerelease)
                {
                    releases.Add(new(id, tag, version, prerelease, "", null));
                    continue;
                }
                var published = entry.GetProperty("published_at");
                if (published.ValueKind != JsonValueKind.String || !published.TryGetDateTimeOffset(out _)) continue;
                var notes = entry.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
                if (notes.Length > 4096) notes = notes[..4096];
                Asset[]? assets = null;
                try { assets = ReadAssets(entry.GetProperty("assets"), tag); }
                catch (Exception error) when (error is ReleaseValidationException or JsonException or InvalidOperationException or
                    KeyNotFoundException or FormatException or OverflowException) { /* Count as rejected, not up-to-date. */ }
                releases.Add(new(id, tag, version, prerelease, notes, assets));
            }
            var explicitNext = ValidatePagination(response.Link, page);
            if (root.GetArrayLength() < 30 && !explicitNext) break;
            if (page == 5) return new(UpdateCheckStatus.Incomplete, Reason: "release-page-budget-exceeded");
        }

        releases.Sort((left, right) => right.Version.CompareTo(left.Version));
        var rejected = 0;
        var incompatible = false;
        var fetched = 0;
        foreach (var release in releases)
        {
            token.ThrowIfCancellationRequested();
            if (release.Assets is null) { rejected++; continue; }
            if (++fetched > 10) return new(UpdateCheckStatus.Incomplete, RejectedReleases: rejected, Reason: "candidate-budget-exceeded");
            try
            {
                var manifestAsset = release.Assets.Single(asset => asset.Name == "update-manifest.json");
                var signatureAsset = release.Assets.Single(asset => asset.Name == "update-manifest.sig.json");
                var package = release.Assets.Single(asset => asset.Name == ManifestRules.ArchiveName(release.Version.ProductVersion));
                var manifestBytes = await _transport.GetAssetAsync(manifestAsset.Id, manifestAsset.BrowserUrl, ManifestRules.ManifestLimit, token).ConfigureAwait(false);
                var signatureBytes = await _transport.GetAssetAsync(signatureAsset.Id, signatureAsset.BrowserUrl, ManifestRules.SignatureLimit, token).ConfigureAwait(false);
                if (!MatchesMetadata(manifestAsset, manifestBytes) || !MatchesMetadata(signatureAsset, signatureBytes))
                    throw new ReleaseValidationException("Asset body does not match listed metadata.");
                var manifest = _verifier.Verify(manifestBytes, signatureBytes);
                if (manifest.Tag != release.Tag || manifest.Version != release.Version.ProductVersion ||
                    (manifest.Channel == "preview") != release.Prerelease || manifest.Package.Size != package.Size ||
                    (package.Digest is not null && package.Digest != "sha256:" + manifest.Package.Sha256))
                    throw new ReleaseValidationException("Signed release identity does not match GitHub metadata.");
                if (!UpdateEligibility.IsCompatible(manifest, context)) { incompatible = true; continue; }
                return new(UpdateCheckStatus.Available, new(release.Id, package.Id, GitHubUrls.ReleasePage(release.Tag),
                    GitHubUrls.AssetApi(package.Id), manifest, manifestBytes, signatureBytes, release.Notes), RejectedReleases: rejected);
            }
            catch (UpdateHttpException error) when (error.Message == "http-404") { rejected++; }
            catch (Exception error) when (error is ReleaseValidationException or JsonException or FormatException or
                CryptographicException or DecoderFallbackException or InvalidOperationException or OverflowException) { rejected++; }
        }
        if (rejected != 0) return new(UpdateCheckStatus.UnableToCheck, RejectedReleases: rejected, Reason: "newer-release-rejected");
        return new(incompatible ? UpdateCheckStatus.NoCompatibleUpdate : UpdateCheckStatus.UpToDate);
    }

    private static Asset[] ReadAssets(JsonElement array, string tag)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 16)
            throw new ReleaseValidationException("Invalid release assets.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<long>();
        var assets = new List<Asset>();
        foreach (var value in array.EnumerateArray())
        {
            var name = value.GetProperty("name").GetString() ?? "";
            var id = value.GetProperty("id").GetInt64();
            if (name.Length is 0 or > 180 || !names.Add(name) || id <= 0 || !ids.Add(id))
                throw new ReleaseValidationException("Duplicate/invalid release asset identity.");
            if (name != "update-manifest.json" && name != "update-manifest.sig.json" && name != ManifestRules.ArchiveName(tag[1..])) continue;
            var size = value.GetProperty("size").GetInt64();
            var limit = name == "update-manifest.json" ? ManifestRules.ManifestLimit : name == "update-manifest.sig.json"
                ? ManifestRules.SignatureLimit : ManifestRules.MaximumFileSize;
            var expectedUrl = GitHubUrls.BrowserAsset(tag, name);
            if (size <= 0 || size > limit || value.GetProperty("state").GetString() != "uploaded" ||
                value.GetProperty("browser_download_url").GetString() != expectedUrl.AbsoluteUri)
                throw new ReleaseValidationException("Unsafe/incomplete release asset.");
            var digest = value.TryGetProperty("digest", out var hash) && hash.ValueKind != JsonValueKind.Null ? hash.GetString() : null;
            if (digest is not null && (!digest.StartsWith("sha256:", StringComparison.Ordinal) || !ManifestRules.IsHash(digest[7..])))
                throw new ReleaseValidationException("Invalid asset digest.");
            assets.Add(new(id, name, size, digest, expectedUrl));
        }
        if (assets.Count != 3) throw new ReleaseValidationException("Required signed update assets are missing.");
        return assets.ToArray();
    }

    private static bool MatchesMetadata(Asset asset, byte[] bytes) => asset.Size == bytes.Length &&
        (asset.Digest is null || asset.Digest == "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private static bool ValidatePagination(string? link, int page)
    {
        if (link is null) return false;
        var hasNext = false;
        foreach (var part in link.Split(','))
        {
            var relation = System.Text.RegularExpressions.Regex.Match(part,
                "(?:^|;)\\s*rel\\s*=\\s*(?:\"(?<value>[^\"]+)\"|(?<value>[a-z]+))(?=\\s*(?:;|$))");
            if (!relation.Success || !relation.Groups["value"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("next")) continue;
            var start = part.IndexOf('<'); var end = part.IndexOf('>');
            if (hasNext || start < 0 || end <= start || !Uri.TryCreate(part[(start + 1)..end], UriKind.Absolute, out var uri) ||
                !GitHubUrls.IsNextListPage(uri, page))
                throw new UpdateHttpException(UpdateCheckStatus.Incomplete, "unsafe-or-excess-pagination");
            hasNext = true;
        }
        return hasNext;
    }

    // Host must stop/await a pending check before disposing the client.
    public void Dispose() { _transport.Dispose(); _gate.Dispose(); }
}
