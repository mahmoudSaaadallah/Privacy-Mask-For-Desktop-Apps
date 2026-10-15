using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using PrivacyMask.Updates;

if (args.Length != 3) throw new ArgumentException("Expected published helper, published peer and measurement report path.");
var helper = Path.GetFullPath(args[0]);
var peer = Path.GetFullPath(args[1]);
var fixture = Directory.CreateTempSubdirectory("PrivacyMask-bootstrap-smoke-").FullName;
var results = new List<JsonElement>();
string sourceHash;
using (var source = File.OpenRead(peer)) sourceHash = Convert.ToHexStringLower(SHA256.HashData(source));
try
{
    var root = Path.Combine(fixture, ManifestRules.AppId);
    PrivateDirectory(root);
    PrivateDirectory(Path.Combine(root, "app"));
    PrivateDirectory(Path.Combine(root, "update-operations"));
    var installId = Guid.NewGuid().ToString("N");
    Write(Path.Combine(root, "managed-install.json"), ContractJson.Write(new ManagedInstallMarker(1, ManifestRules.AppId, installId, 1)));
    Copy(helper, Path.Combine(root, "PrivacyMask.Bootstrap.Prototype.exe"));
    Copy(peer, Path.Combine(root, "app", "PrivacyMask.Readiness.Probe.exe"));
    // Default and arbitrary/apply arguments must not become a launcher.
    await Invoke([], expectReady: false);
    await Invoke(["--apply", installId, Guid.NewGuid().ToString("N")], expectReady: false);
    foreach (var mode in Enum.GetValues<ReadinessProbeMode>())
    {
        var operation = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, "update-operations", "operation-" + operation);
        PrivateDirectory(directory);
        var request = new ReadinessProbeRequest(1, installId, operation, mode, new FileInfo(peer).Length, sourceHash,
            mode == ReadinessProbeMode.Stall ? 500 : 10000);
        Write(Path.Combine(directory, "readiness-probe.json"), RecoveryJson.Write(request, RecoveryJson.Context.ReadinessProbeRequest));
        var result = await Invoke(["--probe", installId, operation], mode == ReadinessProbeMode.Ready);
        Console.WriteLine($"Probe {mode}: {result.GetProperty("outcome").GetString()}");
        results.Add(result);
        // Probe remains non-mutating: no real App file, recovery journal or backups.
        if (File.Exists(Path.Combine(root, "app", "PrivacyMask.App.exe")) || File.Exists(Path.Combine(root, "update-journal.json")))
            throw new InvalidOperationException("Prototype mutated application state.");
        using var available = new ManagedInstallLease(root, installId); // Helper/peer fully exited; lease released.
    }
    var bundleCache = Path.Combine(root, "probe-bundle-cache");
    var report = new
    {
        schemaVersion = 1, os = Environment.OSVersion.VersionString, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        helperBuild = FileVersionInfo.GetVersionInfo(helper).ProductVersion, peerBuild = FileVersionInfo.GetVersionInfo(peer).ProductVersion,
        helperBytes = new FileInfo(helper).Length, peerBytes = new FileInfo(peer).Length,
        bundleCacheBytesAfterMatrix = Directory.Exists(bundleCache) ? Directory.EnumerateFiles(bundleCache, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0,
        limitations = "Developer probe only; OS-reported peak working sets and point-in-time private bytes. No production app startup or Windows 10 qualification.",
        results
    };
    File.WriteAllBytes(Path.GetFullPath(args[2]), JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
    var ready = results.Single(r => r.GetProperty("outcome").GetString() == "Ready");
    Console.WriteLine($"Helper={new FileInfo(helper).Length} bytes; peer={new FileInfo(peer).Length} bytes; protocol={ready.GetProperty("elapsedMs")} ms; wall={ready.GetProperty("processWallMs")} ms; helper peak WS={ready.GetProperty("helperPeakWorkingSetBytes")} bytes; peer peak WS={ready.GetProperty("peerPeakWorkingSetBytes")} bytes.");

    async Task<JsonElement> Invoke(string[] arguments, bool expectReady)
    {
        var start = new ProcessStartInfo(Path.Combine(root, "PrivacyMask.Bootstrap.Prototype.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // Runtime extraction must not leave per-build caches in the user's ordinary .net Temp area.
        start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "probe-bundle-cache");
        var wall = Stopwatch.StartNew();
        using var child = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = child.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = child.StandardError.ReadToEndAsync(deadline.Token);
        try { await child.WaitForExitAsync(deadline.Token); }
        catch { if (!child.HasExited) child.Kill(); throw; }
        var text = await output;
        wall.Stop();
        if (text.Length > 4096 || (await error).Length > 4096) throw new InvalidOperationException("Unbounded probe diagnostics.");
        using var parsed = JsonDocument.Parse(text);
        var result = parsed.RootElement.Clone();
        if ((result.GetProperty("outcome").GetString() == "Ready") != expectReady || (child.ExitCode == 0) != expectReady)
            throw new InvalidOperationException("Unexpected probe outcome: " + text);
        if (result.GetProperty("outcome").GetString() == "CleanupFailed") throw new InvalidOperationException("Probe process cleanup failed.");
        var properties = result.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
        properties["processWallMs"] = JsonSerializer.SerializeToElement(wall.Elapsed.TotalMilliseconds);
        return JsonSerializer.SerializeToElement(properties);
    }
}
finally
{
    var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!fixture.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(fixture).StartsWith("PrivacyMask-bootstrap-smoke-"))
        throw new InvalidOperationException("Unsafe probe fixture cleanup.");
    StagingWorkspace.AssertSafe(fixture);
    Directory.Delete(fixture, recursive: true);
}
static void PrivateDirectory(string path)
{
    using var user = WindowsIdentity.GetCurrent();
    var security = new DirectorySecurity();
    security.SetOwner(user.User!);
    security.SetAccessRuleProtection(true, false);
    security.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.FullControl,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
    new DirectoryInfo(path).Create(security);
}
static void Write(string path, byte[] bytes) { using var output = ManagedInstallLease.CreatePrivateFile(path); output.Write(bytes); }
static void Copy(string source, string target)
{
    using var input = File.OpenRead(source);
    using var output = ManagedInstallLease.CreatePrivateFile(target, 64 * 1024);
    input.CopyTo(output, 64 * 1024);
}
