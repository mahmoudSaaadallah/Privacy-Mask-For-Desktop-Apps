using System.IO.Compression;

namespace PrivacyMask.Updates;

public enum UpdateDownloadPhase { Downloading, Verifying, Extracting, Ready }
public sealed record UpdateDownloadProgress(UpdateDownloadPhase Phase, long Bytes, long TotalBytes);

public sealed class UpdateDownloadException(string reason, bool cleanupIncomplete = false, DateTimeOffset? retryAt = null)
    : Exception(reason)
{
    public string Reason { get; } = reason;
    public bool CleanupIncomplete { get; } = cleanupIncomplete;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

/// <summary>Verified staging snapshot, NOT permission to execute/install. Worker must independently revalidate.</summary>
public sealed class StagedUpdate
{
    private readonly StagingWorkspace _workspace;
    public string DirectoryPath => _workspace.Root;
    public string Version { get; }
    internal StagedUpdate(StagingWorkspace workspace, string version) { _workspace = workspace; Version = version; }
    /// <summary>Deletes only this handle's owned files. False means evidence remains; never recursively deletes.</summary>
    public bool TryDiscard() => _workspace.Cleanup();
}

/// <summary>Anonymous, bounded download/staging only. Host owns the dedicated root and must cancel/await before Dispose.</summary>
public sealed class UpdateStager : IDisposable
{
    private readonly UpdateManifestVerifier _verifier;
    private readonly GitHubTransport _transport;
    private readonly TimeProvider _time;
    private readonly Func<string, long>? _availableSpace;
    private readonly TimeSpan _deadline;
    private int _busy;

    public UpdateStager(UpdateManifestVerifier verifier, TimeProvider? timeProvider = null)
        : this(verifier, timeProvider ?? TimeProvider.System, null, null, TimeSpan.FromMinutes(10)) { }

    internal UpdateStager(UpdateManifestVerifier verifier, TimeProvider time, HttpMessageHandler? handler,
        Func<string, long>? availableSpace = null, TimeSpan? deadline = null)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _time = time;
        _transport = new(time, handler);
        _availableSpace = availableSpace;
        _deadline = deadline ?? TimeSpan.FromMinutes(10);
    }

    public async Task<StagedUpdate> PrepareAsync(UpdateCandidate candidate, UpdateCheckContext context, string stagingRoot,
        IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new UpdateDownloadException("download-in-progress");
        StagingWorkspace? workspace = null;
        using var deadline = new CancellationTokenSource(_deadline, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var ct = linked.Token;
        try
        {
            // Do not let synchronous disk/crypto completions run the preparation on a UI dispatcher.
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            ct.ThrowIfCancellationRequested();
            if (UpdateEligibility.CheckContext(context) is not null) throw new ReleaseValidationException("Unsupported installation context.");
            // Snapshot caller-owned arrays, then authenticate again. Never use mutable Manifest as authority.
            if (candidate.ManifestBytes.Length > ManifestRules.ManifestLimit || candidate.SignatureBytes.Length > ManifestRules.SignatureLimit)
                throw new ReleaseValidationException("Oversized signed metadata.");
            var bytes = candidate.ManifestBytes.ToArray();
            var signature = candidate.SignatureBytes.ToArray();
            var manifest = _verifier.Verify(bytes, signature);
            if (candidate.ReleaseId <= 0 || candidate.PackageAssetId <= 0 ||
                candidate.PackageApiUrl != GitHubUrls.AssetApi(candidate.PackageAssetId) ||
                candidate.ReleasePage != GitHubUrls.ReleasePage(manifest.Tag) ||
                !ContractJson.Write(candidate.Manifest).AsSpan().SequenceEqual(ContractJson.Write(manifest)) ||
                !UpdateEligibility.IsNewerForChannel(manifest.Version, manifest.Channel == "preview", context) ||
                !UpdateEligibility.IsCompatible(manifest, context))
                throw new ReleaseValidationException("Candidate identity or compatibility changed.");
            if (_transport.RetryAt is { } retry && retry > _time.GetUtcNow())
                throw new UpdateHttpException(UpdateCheckStatus.RateLimited, "server-retry-required");
            workspace = new(stagingRoot, manifest, _availableSpace);
            Report(UpdateDownloadPhase.Downloading, 0, manifest.Package.Size);
            var lastReport = _time.GetTimestamp();
            var downloadProgress = new InlineProgress<long>(count =>
            {
                if (count == manifest.Package.Size || _time.GetElapsedTime(lastReport) >= TimeSpan.FromMilliseconds(100))
                {
                    lastReport = _time.GetTimestamp();
                    Report(UpdateDownloadPhase.Downloading, count, manifest.Package.Size);
                }
            });
            await using (var output = workspace.Create("package.partial"))
            {
                await _transport.DownloadAssetAsync(candidate.PackageAssetId,
                    GitHubUrls.BrowserAsset(manifest.Tag, manifest.Package.AssetName), output, manifest.Package, downloadProgress, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            workspace.Rename("package.partial", "package.zip");
            Report(UpdateDownloadPhase.Verifying, 0, manifest.Files[0].Size);
            StagingWorkspace.AssertSafe(Path.Combine(workspace.Root, "package.zip"));
            using (var archive = new FileStream(Path.Combine(workspace.Root, "package.zip"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                UpdatePayload.ValidateArchiveLayout(archive);
                using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
                UpdatePayload.ValidateEntry(zip, manifest.Files[0]);
                Report(UpdateDownloadPhase.Extracting, 0, manifest.Files[0].Size);
                await using var entry = zip.Entries[0].Open();
                await using (var output = workspace.Create("payload.partial"))
                {
                    await UpdatePayload.CopyVerifiedAsync(entry, output, manifest.Files[0].Size, manifest.Files[0].Sha256,
                        cancellationToken: ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
            }
            ct.ThrowIfCancellationRequested();
            StagingWorkspace.AssertSafe(Path.Combine(workspace.Root, "payload.partial"));
            UpdatePayload.InspectExecutable(Path.Combine(workspace.Root, "payload.partial"), manifest.Version, manifest.CommitSha);
            workspace.Rename("payload.partial", "PrivacyMask.App.exe");
            await Save("update-manifest.json", bytes).ConfigureAwait(false);
            await Save("update-manifest.sig.json", signature).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Report(UpdateDownloadPhase.Ready, manifest.Files[0].Size, manifest.Files[0].Size);
            ct.ThrowIfCancellationRequested();
            return new(workspace, manifest.Version);

            async Task Save(string name, byte[] data)
            {
                await using var file = workspace.Create(name);
                await file.WriteAsync(data, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            void Report(UpdateDownloadPhase phase, long done, long total) => progress?.Report(new(phase, done, total));
        }
        catch (Exception ex)
        {
            var incomplete = workspace is not null && !workspace.Cleanup();
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                var canceled = new OperationCanceledException("Update download canceled.", cancellationToken);
                canceled.Data["UpdateCleanupIncomplete"] = incomplete;
                throw canceled;
            }
            var reason = ex switch
            {
                OperationCanceledException => "download-timeout",
                UpdateHttpException http => http.Message,
                ReleaseValidationException or BadImageFormatException or InvalidDataException => "update-validation-failed",
                IOException or UnauthorizedAccessException => "staging-io-failed",
                HttpRequestException => "download-network-failed",
                _ => "download-failed"
            };
            throw new UpdateDownloadException(reason, incomplete, _transport.RetryAt);
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public void Dispose() => _transport.Dispose();
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
