using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using PrivacyMask.Updates;

// Executes ONLY the two purpose-built test peers, never PrivacyMask.App or a real installation.
if (args.Length != 2 || !OperatingSystem.IsWindows()) throw new ArgumentException("Expected two published transaction fixture peers on Windows.");
var fixture = Path.Combine(Path.GetTempPath(), "PrivacyMask-transaction-test-" + Guid.NewGuid().ToString("N"));
CreateFixtureRoot(fixture);
var priorCache = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
var cache = Path.Combine(fixture, "cache");
ManagedInstallLease.CreatePrivateDirectory(cache);
Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", cache);
try
{
    using var assets = new FixtureAssets(fixture, args[1]);
    var cases = new[] { "ReadyNormal", "ReadyMinimized", "StillRunning", "ExitTimeout", "OldCrashed", "DeletedRelease", "NoSpace", "CorruptTarget",
        "ChangedSettings", "Collision", "CancelBefore", "CancelAfterArmed", "LockedApp", "LockedRecovery", "Exit", "WrongNonce",
        "WrongOperation", "Disconnect", "Stall", "ExitAfterReady", "MigrateThenFail", "ReceiptLost", "WrongReceipt", "NoReceipt", "IoAfterReplace",
        "CrashHandoff", "CrashReplacing", "CrashReplaced", "CrashAwaiting", "CrashReady", "CrashCommitTemp", "CrashCommitPromoted" };
    foreach (var scenario in cases)
    {
        var caseRoot = Path.Combine(fixture, "case-" + Guid.NewGuid().ToString("N"));
        try { await RunCase(scenario, args[0], assets, caseRoot); }
        finally
        {
            // Every case releases its exact test processes/handles before this path-scoped cleanup.
            await DeleteOwnedFixture(caseRoot, fixture);
        }
    }
    Console.WriteLine($"Passed {cases.Length} signed real-process transaction cases; test peers only, no user installation or real App execution.");
}
finally
{
    Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", priorCache);
    var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!fixture.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(fixture).StartsWith("PrivacyMask-transaction-test-"))
        throw new InvalidOperationException("Unsafe transaction fixture cleanup.");
    await DeleteOwnedFixture(fixture, Path.GetTempPath());
}

static async Task RunCase(string scenario, string oldExecutable, FixtureAssets assets, string parent)
{
    ManagedInstallLease.CreatePrivateDirectory(parent);
    var root = Path.Combine(parent, ManifestRules.AppId);
    ManagedInstallLease.CreatePrivateDirectory(root);
    ManagedInstallLease.CreatePrivateDirectory(Path.Combine(root, "app"));
    ManagedInstallLease.CreatePrivateDirectory(Path.Combine(root, "update-staging"));
    var installId = Guid.NewGuid().ToString("N");
    Write(Path.Combine(root, "managed-install.json"), ContractJson.Write(new ManagedInstallMarker(1, ManifestRules.AppId, installId, 1)));
    var app = Path.Combine(root, "app", "PrivacyMask.App.exe");
    using (var input = File.OpenRead(oldExecutable))
    using (var output = ManagedInstallLease.CreatePrivateFile(app, 65536)) { await input.CopyToAsync(output); output.Flush(true); }
    var primary = Path.Combine(root, "settings.v1.json");
    var secondary = primary + ".bak";
    var originalSettings = Encoding.UTF8.GetBytes("{\"fixtureVerifier\":\"private opaque saved bytes\"}");
    // Failed migration must restore absence too, not manufacture a default settings file.
    var absent = scenario == "MigrateThenFail";
    if (!absent) { Write(primary, originalSettings); Write(secondary, []); }
    var mode = scenario switch {
        "Exit" or "WrongNonce" or "WrongOperation" or "Disconnect" or "Stall" or "ExitAfterReady" or "MigrateThenFail" or "WrongReceipt" or "NoReceipt" => scenario,
        "LockedRecovery" => "Exit", _ => "Ready" };
    Write(Path.Combine(root, "fixture-mode.txt"), Encoding.UTF8.GetBytes(mode));
    using var lease = new ManagedInstallLease(root, installId);
    using var handler = new FixtureHandler(assets);
    using var stager = new UpdateStager(assets.Verifier, TimeProvider.System, new FixtureHandler(assets));
    using var discovery = new GitHubReleaseClient(assets.Verifier, TimeProvider.System, handler);
    var context = new UpdateCheckContext("0.0.0", UpdateChannel.Preview, "win-x64", Environment.OSVersion.Version.Build, 1, 12, InstallationKind.Managed);
    var stage = await stager.PrepareAsync(assets.Candidate, context, Path.Combine(root, "update-staging"));
    var operationId = Path.GetFileName(stage.DirectoryPath)["operation-".Length..];
    var prepared = await new RecoveryPreparer(lease, assets.Verifier, discovery).PrepareAsync(operationId, context);
    var operation = lease.OperationDirectory(operationId);
    // A separate exit proof holds the exact OS handle even after old process exits.
    using var old = Process.Start(new ProcessStartInfo(app, "--fixture-old") {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, WorkingDirectory = root })!;
    Process? replacement = null;
    FileStream? locked = null;
    using var cancellation = new CancellationTokenSource();
    try
    {
        Check(await old.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "fixture-ready", scenario + ": old fixture startup");
        using var exit = AppProcessExit.Capture(lease, operationId, old.Id);
        var hooks = new TransactionTestHooks(point => {
            if (scenario == "CancelAfterArmed" && point == TransactionCheckpoint.HandoffSaved) cancellation.Cancel();
            if (scenario == "LockedApp" && point == TransactionCheckpoint.Validated)
                locked = new FileStream(app, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (scenario == "LockedRecovery" && point == TransactionCheckpoint.ChildStarted)
                locked = new FileStream(app, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (scenario == "IoAfterReplace" && point == TransactionCheckpoint.PayloadReplaced) throw new IOException("sensitive fixture path");
            if (scenario == "ReceiptLost" && point == TransactionCheckpoint.CommittedSaved) throw new IOException("private receipt failure");
            if ((scenario, point) is ("CrashHandoff", TransactionCheckpoint.HandoffSaved) or
                ("CrashReplacing", TransactionCheckpoint.ReplacingSaved) or ("CrashReplaced", TransactionCheckpoint.PayloadReplaced) or
                ("CrashAwaiting", TransactionCheckpoint.AwaitingReadinessSaved) or ("CrashReady", TransactionCheckpoint.ReadyReceived)) throw new FixtureCrash();
        }, child => {
            replacement = Process.GetProcessById(child.Id);
            _ = PipeProcessIdentity.Capture(replacement, app); // Retain an independent handle for exact fixture cleanup.
        }, (step, point) => {
            if ((scenario == "CrashCommitTemp" && step == RecoveryStep.Committed && point == JournalCheckpoint.TempFlushed) ||
                (scenario == "CrashCommitPromoted" && step == RecoveryStep.Committed && point == JournalCheckpoint.Promoted)) throw new FixtureCrash();
        }, ReadyTimeout: TimeSpan.FromSeconds(5));
        var worker = new UpdateTransaction(lease, assets.Verifier, discovery, TimeProvider.System,
            availableSpace: scenario == "NoSpace" ? _ => 0 : null, hooks: hooks);
        if (scenario == "StillRunning")
        {
            Check((await worker.ApplyAsync(operationId, context, exit, false)).Outcome == TransactionOutcome.Rejected && !old.HasExited,
                scenario + ": live app never replaced or killed");
        }
        if (scenario == "ExitTimeout")
        {
            try { await exit.WaitAsync(TimeSpan.FromMilliseconds(250)); throw new InvalidOperationException("Expected exit timeout."); }
            catch (TimeoutException) { Check(!old.HasExited, "timeout does not kill old app"); }
        }
        await old.StandardInput.WriteLineAsync(scenario == "OldCrashed" ? "invalid-exit" : "exit");
        await old.StandardInput.FlushAsync();
        if (scenario == "OldCrashed")
        {
            try { await exit.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Expected unclean exit rejection."); }
            catch (ReleaseValidationException) { }
            Check((await worker.ApplyAsync(operationId, context, exit, false)).Outcome == TransactionOutcome.Rejected, "abnormal old exit does not authorize replacement");
            Check(new RecoveryJournalStore(lease).Read() == prepared && RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize) == prepared.OriginalApp,
                "abnormal old exit leaves journal/app alone");
            Console.WriteLine("Transaction " + scenario + ": passed");
            return;
        }
        await exit.WaitAsync(TimeSpan.FromSeconds(10));
        Check(old.HasExited && old.ExitCode == 0, scenario + ": exact old process exited cooperatively");
        if (scenario is "StillRunning" or "ExitTimeout")
        {
            Check(new RecoveryRestorer(lease).Recover().Outcome == RecoveryOutcome.Recovered, "unchanged prepared recovery");
            Console.WriteLine("Transaction " + scenario + ": passed");
            return;
        }
        if (scenario == "DeletedRelease") handler.Deleted = true;
        if (scenario == "CorruptTarget") File.AppendAllText(Path.Combine(operation, "target-app.exe"), "corrupt");
        if (scenario == "ChangedSettings") File.AppendAllText(primary, "external edit");
        if (scenario == "Collision") Write(Path.Combine(operation, "displaced-original-app.exe"), [1]);
        if (scenario == "CancelBefore") cancellation.Cancel();
        TransactionResult? result = null;
        if (scenario.StartsWith("Crash", StringComparison.Ordinal))
        {
            try { result = await worker.ApplyAsync(operationId, context, exit, false, cancellation.Token); throw new InvalidOperationException("Expected logical interruption."); }
            catch (FixtureCrash) { }
            Check(replacement is null || replacement.HasExited || scenario == "CrashCommitPromoted", "uncommitted test child stopped before recovery");
            var expected = scenario == "CrashCommitPromoted" ? RecoveryOutcome.Committed : RecoveryOutcome.Recovered;
            Check(new RecoveryRestorer(lease).Recover().Outcome == expected, scenario + ": recovery observes durable state, not scratch");
        }
        else
        {
            result = await worker.ApplyAsync(operationId, context, exit, scenario == "ReadyMinimized", cancellation.Token);
            var expected = scenario switch {
                "ReadyNormal" or "ReadyMinimized" or "CancelAfterArmed" => TransactionOutcome.Committed,
                "ReceiptLost" or "WrongReceipt" or "NoReceipt" => TransactionOutcome.CommittedReceiptLost,
                "DeletedRelease" or "NoSpace" or "CorruptTarget" or "ChangedSettings" or "Collision" or "CancelBefore" => TransactionOutcome.Rejected,
                "LockedRecovery" => TransactionOutcome.RepairRequired, _ => TransactionOutcome.RecoveredFiles };
            Check(result.Outcome == expected, scenario + ": expected " + expected + ", actual " + result.Outcome + "/" + result.Reason);
            Check(result.Reason is null || !result.Reason.Contains("sensitive"), "bounded failure reasons");
            if (expected == TransactionOutcome.Rejected)
            {
                Check(new RecoveryJournalStore(lease).Read() == prepared, "preflight does not advance journal");
                Check(RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize) == prepared.OriginalApp, "preflight leaves old payload alone");
                if (scenario == "ChangedSettings") Check(File.ReadAllText(primary).EndsWith("external edit"), "external settings edit never undone on rejection");
                Console.WriteLine("Transaction " + scenario + ": passed");
                return;
            }
            if (expected == TransactionOutcome.RepairRequired)
            {
                locked!.Dispose(); locked = null;
                Check(new RecoveryRestorer(lease).Recover().Outcome == RecoveryOutcome.Recovered, "repair retry after exact lock release");
            }
        }
        var final = new RecoveryJournalStore(lease).Read()!;
        if (final.Step == RecoveryStep.Committed)
        {
            Check(RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize) == prepared.TargetApp, "committed target retained");
            if (result?.Outcome == TransactionOutcome.Committed)
            {
                var receipt = Path.Combine(root, "fixture-receipt.txt");
                using var receiptDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!File.Exists(receipt)) await Task.Delay(20, receiptDeadline.Token);
                Check(File.ReadAllText(receipt) == "committed:" + (scenario == "ReadyMinimized" ? "minimized" : "normal"), "durable commit precedes receipt and startup preference preserved");
                Check(replacement is not null && !replacement.HasExited, "worker leaves committed child running");
            }
        }
        else
        {
            Check(final.Step == RecoveryStep.Recovered, "uncommitted work restored, not relabeled success");
            Check(RecoveryRestorer.Inspect(app, ManifestRules.MaximumFileSize) == prepared.OriginalApp, "matching old app restored");
            Check(absent ? !File.Exists(primary) && !File.Exists(secondary) :
                File.ReadAllBytes(primary).SequenceEqual(originalSettings) && new FileInfo(secondary).Length == 0, "exact old settings/absence restored");
        }
        Check(RecoveryRestorer.Inspect(Path.Combine(operation, "original-app.exe"), ManifestRules.MaximumFileSize) == prepared.OriginalApp, "last viable baseline never consumed");
        Console.WriteLine("Transaction " + scenario + ": passed");
    }
    finally
    {
        locked?.Dispose();
        Write(Path.Combine(root, "fixture-stop"), [1]);
        if (replacement is not null)
        {
            try { await replacement.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { if (!replacement.HasExited) replacement.Kill(false); await replacement.WaitForExitAsync(); }
            replacement.Dispose();
        }
        if (!old.HasExited) { await old.StandardInput.WriteLineAsync("exit"); await old.StandardInput.FlushAsync(); await old.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}

static void CreateFixtureRoot(string path)
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    using var identity = WindowsIdentity.GetCurrent();
    var security = new DirectorySecurity();
    security.SetOwner(identity.User!); security.SetAccessRuleProtection(true, false);
    security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
    new DirectoryInfo(path).Create(security);
    ManagedInstallLease.AssertPrivate(path);
}
static async Task DeleteOwnedFixture(string path, string parent)
{
    var resolved = Path.GetFullPath(path);
    var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
    var leaf = Path.GetFileName(resolved);
    if (!resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) ||
        !(leaf.StartsWith("case-", StringComparison.Ordinal) || leaf.StartsWith("PrivacyMask-transaction-test-", StringComparison.Ordinal)))
        throw new InvalidOperationException("Unsafe owned fixture cleanup.");
    // Process exit does not require antivirus/image-cache handles to release immediately.
    // Retry ONLY owned Temp cleanup, never alter ACLs/attributes or hide a persistent lock.
    var timer = Stopwatch.StartNew();
    while (true)
    {
        StagingWorkspace.AssertSafe(resolved);
        try { if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true); return; }
        catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && timer.Elapsed < TimeSpan.FromSeconds(10))
        { await Task.Delay(200); }
    }
}
static void Write(string path, byte[] bytes) { using var file = ManagedInstallLease.CreatePrivateFile(path); file.Write(bytes); file.Flush(true); }
static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Transaction smoke: " + label); }
internal sealed class FixtureCrash : Exception;

internal sealed class FixtureAssets : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal string ArchivePath { get; }
    internal byte[] ManifestBytes { get; }
    internal byte[] Signature { get; }
    internal UpdateManifest Manifest { get; }
    internal UpdateManifestVerifier Verifier { get; }
    internal UpdateCandidate Candidate { get; }
    internal FixtureAssets(string fixture, string executable)
    {
        var identity = FileVersionInfo.GetVersionInfo(executable).ProductVersion!.Split('+');
        ArchivePath = Path.Combine(fixture, "fixture-package.zip");
        using (var output = ManagedInstallLease.CreatePrivateFile(ArchivePath))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("PrivacyMask.App.exe").Open())
        using (var input = File.OpenRead(executable)) input.CopyTo(entry, 65536);
        Manifest = new(1, ManifestRules.AppId, ManifestRules.Repository, identity[0], "v" + identity[0], identity[1],
            identity[0].Contains('-') ? "preview" : "stable", "win-x64", 19041, 1,
            new(ManifestRules.ArchiveName(identity[0]), new FileInfo(ArchivePath).Length, Hash(ArchivePath)),
            [new("PrivacyMask.App.exe", new FileInfo(executable).Length, Hash(executable))], new(1, 12, 12));
        ManifestRules.Validate(Manifest);
        ManifestBytes = ContractJson.Write(Manifest);
        Signature = ContractJson.Write(new SignatureEnvelope(ManifestRules.Algorithm, "transaction-fixture",
            Convert.ToBase64String(_key.SignData(ManifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))));
        Verifier = new([new("transaction-fixture", Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()))]);
        Candidate = new(10, 103, GitHubUrls.ReleasePage(Manifest.Tag), GitHubUrls.AssetApi(103), Manifest, ManifestBytes, Signature, "offline fixture");
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    public void Dispose() => _key.Dispose();
}

internal sealed class FixtureHandler(FixtureAssets assets) : HttpMessageHandler
{
    internal bool Deleted { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get || request.Headers.Authorization is not null || request.Headers.Contains("Cookie"))
            throw new InvalidOperationException("Invalid fixture request.");
        HttpContent content;
        if (request.RequestUri == GitHubUrls.AssetApi(103)) content = new StreamContent(File.OpenRead(assets.ArchivePath));
        else if (request.RequestUri == GitHubUrls.AssetApi(101)) content = new ByteArrayContent(assets.ManifestBytes);
        else if (request.RequestUri == GitHubUrls.AssetApi(102)) content = new ByteArrayContent(assets.Signature);
        else if (request.RequestUri == GitHubUrls.ListPage(1))
        {
            var m = assets.Manifest;
            var listing = Deleted ? new JsonArray() : new JsonArray(new JsonObject {
                ["id"] = 10, ["tag_name"] = m.Tag, ["draft"] = false, ["prerelease"] = m.Channel == "preview",
                ["published_at"] = "2026-10-15T00:00:00Z", ["body"] = "fixture only",
                ["assets"] = new JsonArray(Asset(101, "update-manifest.json", assets.ManifestBytes.Length),
                    Asset(102, "update-manifest.sig.json", assets.Signature.Length), Asset(103, m.Package.AssetName, m.Package.Size)) });
            content = new StringContent(listing.ToJsonString());
        }
        else throw new InvalidOperationException("Unexpected fixture URI.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        JsonObject Asset(long id, string name, long size) => new() { ["id"] = id, ["name"] = name, ["size"] = size, ["state"] = "uploaded",
            ["browser_download_url"] = GitHubUrls.BrowserAsset(assets.Manifest.Tag, name).AbsoluteUri };
    }
}
