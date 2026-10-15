using System.Net;
using System.Security.Cryptography;
using PrivacyMask.Updates;

// Offline production-path fixture. Reads test-signed assets, never executes the EXE.
if (args.Length != 4) throw new ArgumentException("Expected asset directory, public test policy, version and commit.");
var assets = Path.GetFullPath(args[0]);
var policy = ContractJson.Read<ReleasePolicy>(ReadSmall(args[1], 64 * 1024), 64 * 1024);
var verifier = new UpdateManifestVerifier(policy.TrustedKeys);
var manifestBytes = ReadSmall(Path.Combine(assets, "update-manifest.json"), ManifestRules.ManifestLimit);
var signature = ReadSmall(Path.Combine(assets, "update-manifest.sig.json"), ManifestRules.SignatureLimit);
var manifest = verifier.Verify(manifestBytes, signature);
Check(manifest.Version == args[2] && manifest.CommitSha == args[3], "fixture source identity");
var candidate = new UpdateCandidate(1, 7, GitHubUrls.ReleasePage(manifest.Tag), GitHubUrls.AssetApi(7),
    manifest, manifestBytes, signature, "");
var context = new UpdateCheckContext("0.0.0", UpdateChannel.Preview, "win-x64", 99999, 1, 12, InstallationKind.Managed);
var fixture = Directory.CreateTempSubdirectory("PrivacyMask-stage-smoke-").FullName;
try
{
    var root = Path.Combine(fixture, "update-staging");
    Directory.CreateDirectory(root);
    foreach (var declaredLength in new[] { true, false })
    {
        using var handler = new FileAssetHandler(Path.Combine(assets, manifest.Package.AssetName), declaredLength);
        using var stager = new UpdateStager(verifier, TimeProvider.System, handler);
        var phases = new List<UpdateDownloadProgress>();
        var stage = await stager.PrepareAsync(candidate, context, root, new InlineProgress(phases.Add));
        Check(handler.Requests == 1, "one anonymous fixed asset request");
        Check(Directory.GetFiles(stage.DirectoryPath).Length == 4, "only ZIP, EXE and two signed metadata files");
        Check(!Directory.EnumerateFiles(stage.DirectoryPath, "*.partial").Any(), "no partial marks ready");
        using (var executable = File.OpenRead(Path.Combine(stage.DirectoryPath, "PrivacyMask.App.exe")))
            Check(Convert.ToHexStringLower(SHA256.HashData(executable)) == manifest.Files[0].Sha256, "real extracted EXE hash");
        UpdatePayload.InspectExecutable(Path.Combine(stage.DirectoryPath, "PrivacyMask.App.exe"), args[2], args[3]);
        Check(File.ReadAllBytes(Path.Combine(stage.DirectoryPath, "update-manifest.json")).SequenceEqual(manifestBytes), "exact signed bytes preserved");
        Check(phases.Last().Phase == UpdateDownloadPhase.Ready && phases.Any(p => p.Phase == UpdateDownloadPhase.Extracting), "stage transitions");
        Check(phases.Where(p => p.Phase == UpdateDownloadPhase.Downloading).All(p => p.Bytes <= p.TotalBytes), "bounded progress");
        Check(stage.TryDiscard() && stage.TryDiscard(), "owned idempotent discard");
        Check(!Directory.EnumerateFileSystemEntries(root).Any(), "no stage residue");
    }
    Console.WriteLine("Passed real-EXE download/staging with declared and unknown length; offline, no execution or installation.");
}
finally
{
    var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!fixture.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(fixture).StartsWith("PrivacyMask-stage-smoke-"))
        throw new InvalidOperationException("Unsafe smoke cleanup.");
    StagingWorkspace.AssertSafe(fixture);
    Directory.Delete(fixture, recursive: true);
}

static byte[] ReadSmall(string path, int limit)
{
    using var file = File.OpenRead(path);
    if (file.Length is <= 0 || file.Length > limit) throw new InvalidDataException("Fixture metadata bounds.");
    var bytes = new byte[(int)file.Length];
    file.ReadExactly(bytes);
    return bytes;
}
static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Smoke failed: " + label); }
sealed class InlineProgress(Action<UpdateDownloadProgress> report) : IProgress<UpdateDownloadProgress>
{
    public void Report(UpdateDownloadProgress value) => report(value);
}
sealed class FileAssetHandler(string path, bool declaredLength) : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != GitHubUrls.AssetApi(7) || request.Headers.Authorization is not null ||
            request.Headers.Contains("Cookie") || request.Headers.Range is not null)
            throw new InvalidOperationException("Unexpected smoke request.");
        Requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new FileAssetContent(path, declaredLength) });
    }
}
sealed class FileAssetContent(string path, bool declaredLength) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = new FileInfo(path).Length; return declaredLength; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException("No memory buffering in this fixture.");
    protected override Task<Stream> CreateContentReadStreamAsync() => Open();
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct) => Open();
    private Task<Stream> Open() => Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous));
}
