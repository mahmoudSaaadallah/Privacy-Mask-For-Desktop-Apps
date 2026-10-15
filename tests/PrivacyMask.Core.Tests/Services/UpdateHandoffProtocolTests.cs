using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateHandoffProtocolTests
{
    private static HandoffSession Session() => new(Guid.NewGuid(), Guid.NewGuid());

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(22)]
    [InlineData(38)]
    [InlineData(69)]
    public void RejectsModifiedHeaderRoleIdentityAndChallenge(int offset)
    {
        var session = Session();
        var challenge = RandomNumberGenerator.GetBytes(32);
        var frame = HandoffWire.Encode(HandoffMessage.ExitRequest, session, challenge);
        Assert.Equal(HandoffWire.FrameSize, frame.Length);
        Assert.Equal(challenge,
            HandoffWire.Validate(frame, HandoffMessage.ExitRequest, session, challenge));

        frame[offset] ^= 1;

        Assert.Throws<ReleaseValidationException>(() =>
            HandoffWire.Validate(frame, HandoffMessage.ExitRequest, session, challenge));
    }

    [Fact]
    public async Task BoundsFrameReadsAndRejectsReplay()
    {
        using var truncated = new MemoryStream(new byte[HandoffWire.FrameSize - 1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            HandoffWire.ReadAsync(truncated, CancellationToken.None));
        using var full = new MemoryStream(new byte[10_000]);
        Assert.Equal(HandoffWire.FrameSize,
            (await HandoffWire.ReadAsync(full, CancellationToken.None)).Length);
        Assert.Equal(HandoffWire.FrameSize, full.Position);

        var session = Session();
        var challenge = RandomNumberGenerator.GetBytes(32);
        var response = HandoffWire.Encode(HandoffMessage.ExitRequest, session, challenge);
        Assert.Throws<ReleaseValidationException>(() =>
            HandoffWire.Validate(response, HandoffMessage.ExitRequest, Session(), challenge));
        Assert.Throws<ReleaseValidationException>(() =>
            HandoffWire.Validate(response, HandoffMessage.ExitRequest, session,
                RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<ReleaseValidationException>(() =>
            HandoffWire.Encode((HandoffMessage)99, session, challenge));
        Assert.Throws<ReleaseValidationException>(() =>
            HandoffWire.Encode(HandoffMessage.ExitRequest,
                new(Guid.Empty, Guid.NewGuid()), challenge));
    }

    [Fact]
    public async Task ExactPeersCompleteOneAuthenticatedHandoff()
    {
        using var process = Process.GetCurrentProcess();
        var identity = PipeProcessIdentity.Capture(process, process.MainModule!.FileName);
        var session = Session();
        using var server = new UpdateHandoffServer(session);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var receive = server.ReceiveExitRequestAsync(identity, deadline.Token);
        using var client = await UpdateHandoffClient.ConnectAsync(
            server.PipeName, session, identity, deadline.Token);
        var request = client.RequestExitAsync(deadline.Token);

        await receive;
        Assert.False(request.IsCompleted);
        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            server.ReceiveExitRequestAsync(identity, deadline.Token));
        await server.AcceptAsync(deadline.Token);
        await request;

        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            server.AcceptAsync(deadline.Token));
        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            client.RequestExitAsync(deadline.Token));
    }

    [Fact]
    public async Task CanceledReceiveCannotBeAcceptedOrReused()
    {
        using var process = Process.GetCurrentProcess();
        var identity = PipeProcessIdentity.Capture(process, process.MainModule!.FileName);
        using var server = new UpdateHandoffServer(Session());
        using var cancellation = new CancellationTokenSource();

        var receive = server.ReceiveExitRequestAsync(identity, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive);
        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            server.AcceptAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            server.ReceiveExitRequestAsync(identity, CancellationToken.None));
    }

    [Fact]
    public async Task WrongPipePeerReceivesNoChallenge()
    {
        using var process = Process.GetCurrentProcess();
        var identity = PipeProcessIdentity.Capture(process, process.MainModule!.FileName) with
        {
            ProcessId = int.MaxValue
        };
        using var server = new UpdateHandoffServer(Session());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receive = server.ReceiveExitRequestAsync(identity, deadline.Token);
        using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        await client.ConnectAsync(deadline.Token);

        await Assert.ThrowsAsync<ReleaseValidationException>(() => receive);
        using var noResponse = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.ReadExactlyAsync(new byte[1], noResponse.Token));
        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            server.AcceptAsync(deadline.Token));
    }

    [Fact]
    public async Task ClientRejectsInvalidPipeAndChangedApplicationIdentity()
    {
        using var process = Process.GetCurrentProcess();
        var identity = PipeProcessIdentity.Capture(process, process.MainModule!.FileName);
        var session = Session();

        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            UpdateHandoffClient.ConnectAsync("PrivacyMask.UpdateHandoff.invalid",
                session, identity, CancellationToken.None));
        await Assert.ThrowsAsync<ReleaseValidationException>(() =>
            UpdateHandoffClient.ConnectAsync(
                UpdateHandoffServer.PipePrefix + Guid.NewGuid().ToString("N"), session,
                identity with { CreationUtcTicks = identity.CreationUtcTicks + 1 },
                CancellationToken.None));
    }
}
