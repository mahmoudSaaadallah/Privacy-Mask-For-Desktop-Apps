using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using PrivacyMask.Updates;

// TEST executable only, with app-compatible version resources for ephemeral signed fixtures.
// No WPF, captures, tray, singleton, registry, real settings migration or normal default launch.
try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    if (args.Length == 5 && args[0] == "--fixture-handoff-old")
    {
        // This peer is test-only: the harness identity is explicit; no production App accepts these switches.
        using var host = Process.GetProcessById(int.Parse(args[3], CultureInfo.InvariantCulture));
        var handoffHostIdentity = new PipeProcessIdentity(host.Id, long.Parse(args[4], CultureInfo.InvariantCulture), host.MainModule!.FileName!);
        using var server = new UpdateHandoffServer(new(Guid.ParseExact(args[1], "N"), Guid.ParseExact(args[2], "N")));
        Console.WriteLine(server.PipeName);
        var receive = server.ReceiveExitRequestAsync(handoffHostIdentity, deadline.Token);
        // Console's synchronized reader can block before returning its async task. Keep this
        // test-only cancellation channel off the thread driving authenticated acceptance.
        var cancel = Task.Factory.StartNew(Console.In.ReadLine, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (await Task.WhenAny(receive, cancel) != receive)
        {
            deadline.Cancel();
            try { await receive; } catch (OperationCanceledException) { }
            return 0;
        }
        await receive;
        await server.AcceptAsync(deadline.Token);
        return 0;
    }
    if (args.Length == 1 && args[0] == "--fixture-old")
    {
        Console.WriteLine("fixture-ready");
        return await Console.In.ReadLineAsync(deadline.Token) == "exit" ? 0 : 31;
    }
    if (args.Length != 7 || args[0] != "--update-start" || !args[1].StartsWith("PrivacyMask.UpdateProbe.", StringComparison.Ordinal) ||
        !RecoveryRules.IsId(args[1]["PrivacyMask.UpdateProbe.".Length..]) || !RecoveryRules.IsId(args[2]) || !RecoveryRules.IsId(args[3]) ||
        !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) ||
        !long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out var parentTime) || args[6] is not ("minimized" or "normal")) return 31;
    var root = Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath!)!)!;
    var modeBytes = ManagedInstallLease.ReadBounded(Path.Combine(root, "fixture-mode.txt"), 64);
    var mode = Encoding.UTF8.GetString(modeBytes);
    if (mode == "Exit") return 31;
    if (mode == "Stall") { await Task.Delay(Timeout.Infinite, deadline.Token); return 31; }
    if (mode == "MigrateThenFail")
    {
        foreach (var name in new[] { "settings.v1.json", "settings.v1.json.bak" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path)) File.WriteAllText(path, "test failed migrated bytes");
            else { using var file = ManagedInstallLease.CreatePrivateFile(path); file.Write("test failed migrated bytes"u8); }
        }
        return 31;
    }
    if (mode is not ("Ready" or "WrongNonce" or "WrongOperation" or "Disconnect" or "ExitAfterReady" or "WrongReceipt" or "NoReceipt")) return 31;
    using var parent = Process.GetProcessById(parentId);
    _ = parent.SafeHandle;
    // Fixture parent is the smoke host, NOT a production stable launcher. Real App must pin its launcher path.
    var identity = new PipeProcessIdentity(parentId, parentTime, parent.MainModule!.FileName!);
    using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(deadline.Token);
    identity.ValidatePipe(pipe, serverSide: false);
    if (mode == "Disconnect") return 31;
    var session = new ReadinessSession(Guid.ParseExact(args[2], "N"), Guid.ParseExact(args[3], "N"));
    var challenge = ReadinessWire.Validate(await ReadinessWire.ReadAsync(pipe, deadline.Token), ReadinessMessage.Challenge, session);
    var response = challenge.ToArray();
    if (mode == "WrongNonce") response[0] ^= 1;
    await pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Ready,
        mode == "WrongOperation" ? session with { OperationId = Guid.NewGuid() } : session, response), deadline.Token);
    if (mode == "ExitAfterReady") return 31;
    ReadinessWire.Validate(await ReadinessWire.ReadAsync(pipe, deadline.Token), ReadinessMessage.Accepted, session, challenge);
    var journal = ContractJson.Read<RecoveryJournal>(ManagedInstallLease.ReadBounded(Path.Combine(root, "update-journal.json"), RecoveryRules.JournalLimit), RecoveryRules.JournalLimit);
    if (journal.InstallId != args[2] || journal.OperationId != args[3] || journal.Step != RecoveryStep.Committed) return 31;
    using (var receipt = ManagedInstallLease.CreatePrivateFile(Path.Combine(root, "fixture-receipt.txt")))
        receipt.Write(Encoding.UTF8.GetBytes("committed:" + args[6]));
    if (mode == "WrongReceipt") challenge[0] ^= 1;
    if (mode != "NoReceipt") await pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Receipt, session, challenge), deadline.Token);
    // Stay alive until the harness releases this exact test instance; no background service remains.
    while (!File.Exists(Path.Combine(root, "fixture-stop"))) await Task.Delay(20, deadline.Token);
    return 0;
}
catch (Exception) { return 31; } // Bounded test exit only, never log challenge/settings/paths.
