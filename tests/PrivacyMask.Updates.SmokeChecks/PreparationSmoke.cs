using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using PrivacyMask.Updates;

internal static class PreparationSmoke
{
    internal static async Task RunAsync(string fixture, string assets, string baselineExe, UpdateCandidate candidate,
        UpdateCheckContext context, UpdateManifestVerifier verifier)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = Path.Combine(fixture, ManifestRules.AppId);
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var user = identity.User ?? throw new InvalidOperationException("Fixture SID unavailable.");
            var security = new DirectorySecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).Create(security);
        }
        ManagedInstallLease.AssertPrivate(root);
        var installId = Guid.NewGuid().ToString("N");
        Write(Path.Combine(root, "managed-install.json"), ContractJson.Write(new ManagedInstallMarker(1, ManifestRules.AppId, installId, 1)));
        ManagedInstallLease.CreatePrivateDirectory(Path.Combine(root, "app"));
        ManagedInstallLease.CreatePrivateDirectory(Path.Combine(root, "update-staging"));
        var app = Path.Combine(root, "app", "PrivacyMask.App.exe");
        using (var input = File.OpenRead(baselineExe))
        using (var output = ManagedInstallLease.CreatePrivateFile(app, 65536))
        { await input.CopyToAsync(output); output.Flush(true); }
        var primary = Path.Combine(root, "settings.v1.json");
        var secondary = primary + ".bak";
        // Opaque snapshot verification, not App settings/PIN semantic qualification.
        var oldSettings = Encoding.UTF8.GetBytes("{\"fixture\":\"private settings bytes, never in journal\"}");
        Write(primary, oldSettings);
        Write(secondary, []);
        using var lease = new ManagedInstallLease(root, installId);
        var before = RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize);
        context = context with { WindowsBuild = Environment.OSVersion.Version.Build };
        using var packageHandler = new FileAssetHandler(Path.Combine(assets, candidate.Manifest.Package.AssetName), false);
        using var stager = new UpdateStager(verifier, TimeProvider.System, packageHandler);
        var stage = await stager.PrepareAsync(candidate, context, Path.Combine(root, "update-staging"));
        var operationId = Path.GetFileName(stage.DirectoryPath)["operation-".Length..];
        using var discoveryHandler = new PreparationReleaseHandler(candidate);
        using var discovery = new GitHubReleaseClient(verifier, TimeProvider.System, discoveryHandler);
        var preparer = new RecoveryPreparer(lease, verifier, discovery); // No inspection/budget/clock bypass.
        var record = await preparer.PrepareAsync(operationId, context);
        var operation = lease.OperationDirectory(operationId);
        Check(record.Step == RecoveryStep.Prepared && record.OriginalVersion == "0.0.0", "real baseline PE identity");
        Check(record.OriginalApp == before && RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize) == before, "real app remains unchanged");
        Check(RecoveryRestorer.Inspect(Path.Combine(operation, "target-app.exe"), ManifestRules.MaximumFileSize) == record.TargetApp, "real target copied and verified");
        Check(File.ReadAllBytes(primary).SequenceEqual(oldSettings) && new FileInfo(secondary).Length == 0, "settings unchanged");
        Check(discoveryHandler.Requests == 3, "fresh offline listing and both signed metadata assets");
        Check(File.ReadAllBytes(Path.Combine(operation, "update-manifest.json")).SequenceEqual(candidate.ManifestBytes.ToArray()), "operation exact signed bytes");
        Check(new RecoveryRestorer(lease).Recover().Outcome == RecoveryOutcome.Recovered, "prepared baseline recovery kernel");
        new RecoveryJournalStore(lease).ArchiveTerminal(operationId);
        Check(new RecoveryJournalStore(lease).Read() is null && File.Exists(Path.Combine(operation, "terminal-journal.json")), "terminal history retained");
        Check(stage.TryDiscard(), "staging cleanup independent of retained rollback baseline");
        // The retained target remains independently signed/hash-verified after staging disappears.
        var signed = verifier.Verify(File.ReadAllBytes(Path.Combine(operation, "update-manifest.json")), File.ReadAllBytes(Path.Combine(operation, "update-manifest.sig.json")));
        UpdatePayload.InspectExecutable(Path.Combine(operation, "target-app.exe"), signed.Version, signed.CommitSha);
        Console.WriteLine("Passed real-EXE authenticated preparation: live PE identities, signed bytes, exact baseline/settings copies and terminal archival; offline, no execution or installed-app mutation.");
    }

    private static void Write(string path, byte[] bytes)
    {
        using var file = ManagedInstallLease.CreatePrivateFile(path);
        file.Write(bytes); file.Flush(true);
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("Preparation smoke failed: " + label); }
}

internal sealed class PreparationReleaseHandler(UpdateCandidate candidate) : HttpMessageHandler
{
    internal int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.Headers.Authorization is not null || request.Headers.Contains("Cookie"))
            throw new InvalidOperationException("Unexpected authenticated or non-read-only fixture request.");
        Requests++;
        byte[] data;
        if (request.RequestUri == GitHubUrls.ListPage(1))
        {
            var manifest = candidate.Manifest;
            var listing = new JsonArray(new JsonObject {
                ["id"] = candidate.ReleaseId, ["tag_name"] = manifest.Tag, ["draft"] = false,
                ["prerelease"] = manifest.Channel == "preview", ["published_at"] = "2026-10-15T00:00:00Z", ["body"] = "offline fixture",
                ["assets"] = new JsonArray(
                    Asset(1, "update-manifest.json", candidate.ManifestBytes.Length),
                    Asset(2, "update-manifest.sig.json", candidate.SignatureBytes.Length),
                    Asset(candidate.PackageAssetId, manifest.Package.AssetName, manifest.Package.Size)) });
            data = Encoding.UTF8.GetBytes(listing.ToJsonString());
        }
        else if (request.RequestUri == GitHubUrls.AssetApi(1)) data = candidate.ManifestBytes.ToArray();
        else if (request.RequestUri == GitHubUrls.AssetApi(2)) data = candidate.SignatureBytes.ToArray();
        else throw new InvalidOperationException("Unexpected preparation fixture URI.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });

        JsonObject Asset(long id, string name, long size) => new() {
            ["id"] = id, ["name"] = name, ["size"] = size, ["state"] = "uploaded",
            ["browser_download_url"] = GitHubUrls.BrowserAsset(candidate.Manifest.Tag, name).AbsoluteUri };
    }
}
