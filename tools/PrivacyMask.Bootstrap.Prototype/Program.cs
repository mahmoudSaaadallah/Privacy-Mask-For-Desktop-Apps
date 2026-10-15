using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using PrivacyMask.Updates;

// Developer-only probe host. No --apply/recover/update/default-launch entrypoint exists.
var timer = Stopwatch.StartNew();
var outcome = "InvalidFixture";
var stage = "invocation";
var rejection = "none";
long peerPeak = 0, peerPrivate = 0;
Process? child = null;
Task? exchange = null;
Task? unexpectedExchange = null;
NamedPipeClientStream? unexpected = null;
ReadinessServer? server = null;
CancellationTokenSource? deadline = null;
ManagedInstallLease? lease = null;
FileStream? pinnedPeer = null;
try
{
    if (args.Length != 3 || args[0] != "--probe" || !RecoveryRules.IsId(args[1]) || !RecoveryRules.IsId(args[2]))
        throw new ReleaseValidationException("Probe invocation required.");
    var self = Environment.ProcessPath ?? throw new ReleaseValidationException("Missing probe executable.");
    var root = Path.GetDirectoryName(self)!;
    stage = "fixture";
    lease = new ManagedInstallLease(root, args[1]);
    ManagedInstallLease.AssertPrivate(self);
    // Never interpret pending real recovery state or touch an app executable.
    var journal = Path.Combine(root, "update-journal.json");
    StagingWorkspace.AssertSafe(journal);
    if (File.Exists(journal) || Directory.Exists(journal)) throw new ReleaseValidationException("Probe refuses journal state.");
    var operation = lease.OperationDirectory(args[2]);
    var requestPath = Path.Combine(operation, "readiness-probe.json");
    ManagedInstallLease.AssertPrivate(requestPath);
    var request = RecoveryJson.Read(ManagedInstallLease.ReadBounded(requestPath, 4096), 4096, RecoveryJson.Context.ReadinessProbeRequest);
    if (request.SchemaVersion != 1 || request.InstallId != args[1] || request.OperationId != args[2] || !Enum.IsDefined(request.Mode) ||
        request.PeerSize is <= 0 or > ManifestRules.MaximumFileSize || !ManifestRules.IsHash(request.PeerSha256) ||
        request.DeadlineMilliseconds is < 250 or > 15000) throw new ReleaseValidationException("Invalid probe fixture.");
    var peerPath = Path.Combine(root, "app", "PrivacyMask.Readiness.Probe.exe");
    ManagedInstallLease.AssertPrivate(Path.GetDirectoryName(peerPath)!);
    ManagedInstallLease.AssertPrivate(peerPath);
    pinnedPeer = new FileStream(peerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (pinnedPeer.Length != request.PeerSize || Convert.ToHexStringLower(SHA256.HashData(pinnedPeer)) != request.PeerSha256 ||
        FileVersionInfo.GetVersionInfo(peerPath).ProductName != "PrivacyMask Readiness Probe")
        throw new ReleaseValidationException("Unexpected developer probe payload.");
    var session = new ReadinessSession(Guid.ParseExact(args[1], "N"), Guid.ParseExact(args[2], "N"));
    server = new(session);
    deadline = new(request.DeadlineMilliseconds);
    using var parent = Process.GetCurrentProcess();
    stage = "parent-identity";
    var parentIdentity = PipeProcessIdentity.Capture(parent, self);
    var start = new ProcessStartInfo(peerPath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(peerPath)! };
    foreach (var argument in new[] { "--probe-client", server.PipeName, args[1], args[2],
        parentIdentity.ProcessId.ToString(CultureInfo.InvariantCulture), parentIdentity.CreationUtcTicks.ToString(CultureInfo.InvariantCulture),
        ((int)request.Mode).ToString(CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
    child = Process.Start(start) ?? throw new IOException("Probe did not start.");
    stage = "child-identity";
    var identity = PipeProcessIdentity.Capture(child, peerPath);
    stage = "exchange";
    exchange = server.ReceiveReadyAsync(identity, deadline.Token);
    if (request.Mode == ReadinessProbeMode.WrongProcess)
    {
        // An unexpected same-user PID connects instead of the retained child. It gets no challenge.
        unexpected = new(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await unexpected.ConnectAsync(deadline.Token);
        unexpectedExchange = SpoofAsync(unexpected, session, deadline.Token);
    }
    var exited = child.WaitForExitAsync();
    if (await Task.WhenAny(exchange, exited) != exchange) outcome = "PeerExited";
    else
    {
        await exchange;
        stage = "observation";
        // A syntactically valid frame immediately followed by exit is not readiness.
        var observation = Task.Delay(100, deadline.Token);
        if (await Task.WhenAny(exited, observation) == exited) outcome = "PeerExited";
        else
        {
            await observation;
            identity.ValidateLive();
            child.Refresh();
            peerPeak = child.PeakWorkingSet64;
            peerPrivate = child.PrivateMemorySize64;
            stage = "receipt";
            await server.AcceptAsync(identity, deadline.Token); // Probe receipt only, NEVER a journal commit.
            await child.WaitForExitAsync(deadline.Token);
            outcome = child.ExitCode == 0 ? "Ready" : "PeerExited";
        }
    }
}
catch (OperationCanceledException) { outcome = "TimedOut"; }
catch (ReleaseValidationException exception)
{
    outcome = deadline is null ? "InvalidFixture" : "Rejected";
    rejection = exception.Message switch
    {
        "Unexpected update pipe peer." => "pipe-peer",
        "Update process identity changed." => "process-identity",
        "Invalid readiness response." => "response",
        _ => "validation"
    };
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
{ outcome = "TransportFailure"; }
finally
{
    deadline?.Cancel();
    unexpected?.Dispose();
    server?.Dispose();
    if (exchange is not null) { try { await exchange; } catch (Exception) { /* Observed probe exchange failure. */ } }
    if (unexpectedExchange is not null) { try { await unexpectedExchange; } catch (Exception) { /* Observed rejected test client. */ } }
    if (child is not null)
    {
        try
        {
            // Only the exact developer probe process created above; never a name/PID lookup or process tree.
            if (!child.HasExited) child.Kill(entireProcessTree: false);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception) { outcome = "CleanupFailed"; }
        child.Dispose();
    }
    deadline?.Dispose();
    pinnedPeer?.Dispose();
    lease?.Dispose();
}
using var current = Process.GetCurrentProcess();
Console.WriteLine(FormattableString.Invariant($"{{\"outcome\":\"{outcome}\",\"stage\":\"{stage}\",\"rejection\":\"{rejection}\",\"elapsedMs\":{timer.Elapsed.TotalMilliseconds:F2},\"helperPeakWorkingSetBytes\":{current.PeakWorkingSet64},\"helperPrivateBytesAtEnd\":{current.PrivateMemorySize64},\"peerPeakWorkingSetBytes\":{peerPeak},\"peerPrivateBytesAtAck\":{peerPrivate}}}"));
return outcome == "Ready" ? 0 : 20;

static async Task SpoofAsync(NamedPipeClientStream pipe, ReadinessSession session, CancellationToken ct)
{
    // If the server ever discloses its challenge to the wrong PID, this test client can answer it.
    var nonce = ReadinessWire.Validate(await ReadinessWire.ReadAsync(pipe, ct), ReadinessMessage.Challenge, session);
    await pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Ready, session, nonce), ct);
    ReadinessWire.Validate(await ReadinessWire.ReadAsync(pipe, ct), ReadinessMessage.Accepted, session, nonce);
}
