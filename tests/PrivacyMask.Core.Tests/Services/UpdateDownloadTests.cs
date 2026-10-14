using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.ReleaseTool;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateDownloadTests
{
    [Theory]
    [InlineData("manifest")]
    [InlineData("signature")]
    [InlineData("mutable-manifest")]
    [InlineData("asset-url")]
    [InlineData("release-url")]
    [InlineData("asset-id")]
    [InlineData("release-id")]
    [InlineData("older")]
    [InlineData("portable")]
    [InlineData("schema")]
    [InlineData("os")]
    [InlineData("protocol")]
    [InlineData("channel")]
    public async Task ReauthenticatesBeforeNetworkOrWrites(string change)
    {
        using var f = new DownloadFixture();
        var candidate = f.Candidate;
        var context = f.Trust.Context;
        candidate = change switch
        {
            "manifest" => candidate with { ManifestBytes = candidate.ManifestBytes.ToArray().Concat(new byte[] { 10 }).ToArray() },
            "signature" => candidate with { SignatureBytes = new byte[] { 123, 125 } },
            "mutable-manifest" => candidate with { Manifest = candidate.Manifest with { Version = "9.0.0" } },
            "asset-url" => candidate with { PackageApiUrl = new Uri("https://example.com/package") },
            "release-url" => candidate with { ReleasePage = new Uri("https://example.com/release") },
            "asset-id" => candidate with { PackageAssetId = 0 },
            "release-id" => candidate with { ReleaseId = 0 },
            _ => candidate
        };
        context = change switch
        {
            "older" => context with { InstalledVersion = "9.0.0" },
            "portable" => context with { Installation = InstallationKind.Portable },
            "schema" => context with { SettingsSchema = 13 },
            "os" => context with { WindowsBuild = 10000 },
            "protocol" => context with { UpdaterProtocol = 0 },
            "channel" => context with { Channel = UpdateChannel.Stable },
            _ => context
        };
        using var stager = f.Stager();
        await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(candidate, context, f.Root));
        Assert.Empty(f.Trust.Handler.Requests);
        f.AssertEmpty();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("hash")]
    [InlineData("header")]
    [InlineData("http")]
    [InlineData("network")]
    [InlineData("partial-response")]
    [InlineData("redirect")]
    [InlineData("invalid-pe")]
    public async Task FailedDownloadsLeaveNoPreparedFiles(string failure)
    {
        using var f = new DownloadFixture();
        f.Trust.Handler.Intercept = (_, _) =>
        {
            if (failure == "network") throw new HttpRequestException("sensitive-url-must-not-leak");
            var bytes = failure switch
            {
                "short" => f.Archive[..^1], "long" => f.Archive.Concat(new byte[] { 0 }).ToArray(),
                "hash" => new byte[f.Archive.Length], _ => f.Archive
            };
            var response = new HttpResponseMessage(failure switch
            {
                "http" => HttpStatusCode.NotFound, "partial-response" => HttpStatusCode.PartialContent,
                "redirect" => HttpStatusCode.Redirect, _ => HttpStatusCode.OK
            }) { Content = new StreamContent(new MemoryStream(bytes)) };
            // StreamContent on a non-seekable source tests actual limits, not only headers.
            if (failure is "short" or "long" or "hash") response.Content = new StreamContent(new NonSeekStream(bytes));
            if (failure == "header") response.Content.Headers.ContentLength = bytes.Length + 1;
            if (failure == "redirect") response.Headers.Location = new Uri("https://example.com/untrusted?secret=redacted");
            return Task.FromResult(response);
        };
        using var stager = f.Stager();
        var ex = await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root));
        Assert.False(ex.CleanupIncomplete);
        Assert.DoesNotContain("sensitive", ex.ToString());
        Assert.DoesNotContain("secret", ex.ToString());
        Assert.All(f.Trust.Handler.Requests, request => Assert.False(request.HasCredentials));
        f.AssertEmpty();
    }

    [Theory]
    [InlineData("../outside.exe", 0, false)]
    [InlineData("PrivacyMask.App.exe:stream", 0, false)]
    [InlineData("PrivacyMask.App.exe", 0xa000, false)]
    [InlineData("PrivacyMask.App.exe", 0, true)]
    public async Task RejectsSignedButUnsafeArchives(string path, int unixType, bool duplicate)
    {
        using var f = new DownloadFixture(path, unixType, duplicate);
        using var stager = f.Stager();
        await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root));
        f.AssertEmpty();
        Assert.False(File.Exists(Path.Combine(f.Parent, "outside.exe")));
    }

    [Fact]
    public async Task DiskPreflightAndInvalidRootsDoNotStartDownload()
    {
        using var f = new DownloadFixture();
        using var stager = f.Stager(free: _ => 0);
        await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root));
        Assert.Empty(f.Trust.Handler.Requests);
        f.AssertEmpty();
        using var other = f.Stager();
        foreach (var root in new[] { "relative/update-staging", Path.GetPathRoot(f.Root)!, f.Parent, Path.GetTempPath(), @"\\server\share\update-staging" })
            await Assert.ThrowsAsync<UpdateDownloadException>(() => other.PrepareAsync(f.Candidate, f.Trust.Context, root));
        Assert.Empty(f.Trust.Handler.Requests);
    }

    [Fact]
    public async Task CancellationReleasesGateAndCleansPartialFiles()
    {
        using var f = new DownloadFixture();
        using var cancel = new CancellationTokenSource();
        using var stager = f.Stager();
        var progress = new InlineProgress<UpdateDownloadProgress>(p => { if (p.Bytes > 0) cancel.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root, progress, cancel.Token));
        f.AssertEmpty();
        // Second operation reaches PE validation rather than remaining locked.
        await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root));
        Assert.Equal(2, f.Trust.Handler.Requests.Count);
        f.AssertEmpty();
    }

    [Fact]
    public async Task TimeoutSingleFlightAndServerBackoffAreBounded()
    {
        using var f = new DownloadFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Trust.Handler.Intercept = async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); };
        using var stager = f.Stager(deadline: TimeSpan.FromSeconds(2));
        var pending = stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("download-in-progress", (await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root))).Reason);
        Assert.Equal("download-timeout", (await Assert.ThrowsAsync<UpdateDownloadException>(() => pending)).Reason);
        f.AssertEmpty();
        f.Trust.Handler.Intercept = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var rate = await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root));
        Assert.NotNull(rate.RetryAt);
        var count = f.Trust.Handler.Requests.Count;
        await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root));
        Assert.Equal(count, f.Trust.Handler.Requests.Count);
        f.AssertEmpty();
    }

    [Fact]
    public void WorkspaceIsPrivateAndCleanupNeverDeletesUnknownFiles()
    {
        using var f = new DownloadFixture();
        var workspace = new StagingWorkspace(f.Root, f.Candidate.Manifest);
        var security = new DirectoryInfo(workspace.Root).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var user = WindowsIdentity.GetCurrent().User!;
        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.All(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule => Assert.Equal(user, rule.IdentityReference));
        using (var owned = workspace.Create("partial")) owned.WriteByte(1);
        File.WriteAllText(Path.Combine(workspace.Root, "unknown.txt"), "preserve");
        Assert.False(workspace.Cleanup());
        Assert.True(File.Exists(Path.Combine(workspace.Root, "unknown.txt")));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "partial")));
    }

    [Fact]
    public async Task ReparseRootAndCleanupDoNotFollowJunctions()
    {
        using var f = new DownloadFixture();
        var outside = Path.Combine(f.Parent, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "untouched");
        Directory.Delete(f.Root);
        CreateJunction(f.Root, outside);
        try
        {
            using var stager = f.Stager();
            await Assert.ThrowsAsync<UpdateDownloadException>(() => stager.PrepareAsync(f.Candidate, f.Trust.Context, f.Root));
            Assert.Empty(f.Trust.Handler.Requests);
            Assert.True(File.Exists(sentinel));
        }
        finally { Directory.Delete(f.Root); } // exact fixture junction, not its target
        Directory.CreateDirectory(f.Root);
        var workspace = new StagingWorkspace(f.Root, f.Candidate.Manifest);
        var moved = workspace.Root + "-original";
        Directory.Move(workspace.Root, moved);
        CreateJunction(workspace.Root, outside);
        try
        {
            Assert.False(workspace.Cleanup());
            Assert.True(File.Exists(sentinel));
        }
        finally { Directory.Delete(workspace.Root); }
    }

    private static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
    }

    [Fact]
    public async Task DiskWriteFailureAndInterruptedReadAreNotValidationSuccess()
    {
        using var source = new MemoryStream(new byte[100]);
        using var broken = new BrokenOutput();
        await Assert.ThrowsAsync<IOException>(() => UpdatePayload.CopyVerifiedAsync(source, broken, 100, Hash(new byte[100])));
        using var interrupted = new NonSeekStream(new byte[100], fail: true);
        await Assert.ThrowsAsync<IOException>(() => UpdatePayload.CopyVerifiedAsync(interrupted, Stream.Null, 100, Hash(new byte[100])));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private sealed class BrokenOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => ValueTask.FromException(new IOException("disk full"));
    }
    private sealed class NonSeekStream(byte[] bytes, bool fail = false) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            fail ? ValueTask.FromException<int>(new IOException("interrupted")) : base.ReadAsync(buffer, ct);
    }

    private sealed class DownloadFixture : IDisposable
    {
        public UpdateDiscoveryFixture Trust { get; } = new();
        public string Parent { get; } = Path.Combine(Path.GetTempPath(), "PrivacyMask-download-test-" + Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(Parent, "update-staging");
        public byte[] Archive { get; }
        public UpdateCandidate Candidate { get; }
        public DownloadFixture(string path = "PrivacyMask.App.exe", int unixType = 0, bool duplicate = false)
        {
            Directory.CreateDirectory(Root);
            byte[] payload = new byte[1024];
            RandomNumberGenerator.Fill(payload);
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                var entry = zip.CreateEntry(path);
                entry.ExternalAttributes = unixType << 16;
                using (var output = entry.Open()) output.Write(payload);
                if (duplicate) zip.CreateEntry(path);
            }
            Archive = memory.ToArray();
            Trust.Release(changeManifest: manifest => manifest with
            {
                Package = manifest.Package with { Size = Archive.Length, Sha256 = Hash(Archive) },
                Files = [new("PrivacyMask.App.exe", payload.Length, Hash(payload))]
            });
            var manifestBytes = Trust.Handler.Assets[101];
            var signature = Trust.Handler.Assets[102];
            var manifest = Trust.Verifier.Verify(manifestBytes, signature);
            Candidate = new(10, 103, GitHubUrls.ReleasePage(manifest.Tag), GitHubUrls.AssetApi(103), manifest, manifestBytes, signature, "");
            Trust.Handler.Assets[103] = Archive;
        }
        public UpdateStager Stager(Func<string, long>? free = null, TimeSpan? deadline = null) => new(Trust.Verifier,
            TimeProvider.System, Trust.Handler, free, deadline);
        public void AssertEmpty() => Assert.Empty(Directory.GetFileSystemEntries(Root));
        public void Dispose()
        {
            Trust.Dispose();
            var temp = Path.GetFullPath(Path.GetTempPath());
            if (!Parent.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Parent).StartsWith("PrivacyMask-download-test-"))
                throw new InvalidOperationException("Unsafe fixture cleanup.");
            Directory.Delete(Parent, recursive: true);
        }
    }
}
