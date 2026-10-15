using System.Diagnostics;
using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateReadinessClientTests
{
    [Fact]
    public async Task Client_authenticates_parent_and_receipts_only_a_durable_commit()
    {
        using var fixture = AwaitingFixture();
        using var parent = Process.GetCurrentProcess();
        var parentPath = Environment.ProcessPath!;
        var identity = PipeProcessIdentity.Capture(parent, parentPath);
        var session = new ReadinessSession(Guid.ParseExact(fixture.InstallId, "N"), Guid.ParseExact(fixture.OperationId, "N"));
        using var server = new ReadinessServer(session);
        var request = new UpdateStartRequest(server.PipeName, fixture.InstallId, fixture.OperationId,
            identity.ProcessId, identity.CreationUtcTicks, UpdateStartMode.Minimized);
        var layout = UpdateStartLayout.Open(request, fixture.App, fixture.Root) with { LauncherPath = parentPath };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var receive = server.ReceiveReadyAsync(identity, deadline.Token);
        using var client = await UpdateReadinessClient.ConnectAsync(request, layout, deadline.Token);
        await client.SignalReadyAsync(deadline.Token);
        await receive;

        fixture.Advance(RecoveryStep.Committed);
        await server.AcceptAsync(identity, deadline.Token);
        await client.ConfirmCommittedAsync(deadline.Token);
        await server.ConfirmReceiptAsync(identity, deadline.Token);
    }

    [Fact]
    public async Task Client_refuses_an_acceptance_without_the_committed_journal()
    {
        using var fixture = AwaitingFixture();
        using var parent = Process.GetCurrentProcess();
        var parentPath = Environment.ProcessPath!;
        var identity = PipeProcessIdentity.Capture(parent, parentPath);
        var session = new ReadinessSession(Guid.ParseExact(fixture.InstallId, "N"), Guid.ParseExact(fixture.OperationId, "N"));
        using var server = new ReadinessServer(session);
        var request = new UpdateStartRequest(server.PipeName, fixture.InstallId, fixture.OperationId,
            identity.ProcessId, identity.CreationUtcTicks, UpdateStartMode.Minimized);
        var layout = UpdateStartLayout.Open(request, fixture.App, fixture.Root) with { LauncherPath = parentPath };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var receive = server.ReceiveReadyAsync(identity, deadline.Token);
        using var client = await UpdateReadinessClient.ConnectAsync(request, layout, deadline.Token);
        await client.SignalReadyAsync(deadline.Token);
        await receive;
        await server.AcceptAsync(identity, deadline.Token);

        await Assert.ThrowsAsync<ReleaseValidationException>(() => client.ConfirmCommittedAsync(deadline.Token));
    }

    private static RecoveryFixture AwaitingFixture()
    {
        var fixture = new RecoveryFixture();
        using (var launcher = ManagedInstallLease.CreatePrivateFile(Path.Combine(fixture.Root, "PrivacyMask.Launcher.exe")))
            launcher.WriteByte(1);
        fixture.Advance(RecoveryStep.AwaitingReadiness);
        File.WriteAllBytes(fixture.App, fixture.NewApp);
        File.WriteAllBytes(Path.Combine(fixture.Operation, "displaced-original-app.exe"), fixture.OldApp);
        return fixture;
    }
}
