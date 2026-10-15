using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using PrivacyMask.Updates;

// Fault-capable TEST peer, not PrivacyMask.App and not an implementation of app readiness.
try
{
    if (args.Length != 7 || args[0] != "--probe-client" || !args[1].StartsWith("PrivacyMask.UpdateProbe.", StringComparison.Ordinal) ||
        !RecoveryRules.IsId(args[1]["PrivacyMask.UpdateProbe.".Length..]) || !RecoveryRules.IsId(args[2]) || !RecoveryRules.IsId(args[3]) ||
        !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) ||
        !long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out var parentTime) ||
        !int.TryParse(args[6], NumberStyles.None, CultureInfo.InvariantCulture, out var modeNumber) ||
        !Enum.IsDefined((ReadinessProbeMode)modeNumber)) return 21;
    var mode = (ReadinessProbeMode)modeNumber;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    if (mode is ReadinessProbeMode.Stall or ReadinessProbeMode.WrongProcess) { await Task.Delay(Timeout.Infinite, timeout.Token); return 21; }
    var root = Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath!)!)!;
    using var parent = Process.GetProcessById(parentId);
    _ = parent.SafeHandle;
    var identity = new PipeProcessIdentity(parentId, parentTime + (mode == ReadinessProbeMode.WrongParent ? 1 : 0),
        Path.Combine(root, "PrivacyMask.Bootstrap.Prototype.exe"));
    using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(timeout.Token);
    identity.ValidatePipe(pipe, serverSide: false);
    if (mode == ReadinessProbeMode.Disconnect) return 21;
    var session = new ReadinessSession(Guid.ParseExact(args[2], "N"), Guid.ParseExact(args[3], "N"));
    var challenge = ReadinessWire.Validate(await ReadinessWire.ReadAsync(pipe, timeout.Token), ReadinessMessage.Challenge, session);
    var responseSession = mode == ReadinessProbeMode.WrongOperation ? session with { OperationId = Guid.NewGuid() } : session;
    var responseChallenge = challenge.ToArray();
    if (mode == ReadinessProbeMode.WrongChallenge) responseChallenge[0] ^= 1;
    await pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Ready, responseSession, responseChallenge), timeout.Token);
    if (mode == ReadinessProbeMode.EarlyExit) return 21;
    ReadinessWire.Validate(await ReadinessWire.ReadAsync(pipe, timeout.Token), ReadinessMessage.Accepted, session, challenge);
    return 0;
}
catch (Exception) { return 21; } // No paths, process details, challenge or raw errors to logs.
