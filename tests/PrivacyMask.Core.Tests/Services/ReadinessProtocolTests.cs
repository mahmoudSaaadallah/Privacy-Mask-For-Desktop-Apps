using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ReadinessProtocolTests
{
    private static ReadinessSession Session() => new(Guid.NewGuid(), Guid.NewGuid());

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
        var nonce = RandomNumberGenerator.GetBytes(32);
        var frame = ReadinessWire.Encode(ReadinessMessage.Ready, session, nonce);
        Assert.Equal(70, frame.Length);
        Assert.Equal(nonce, ReadinessWire.Validate(frame, ReadinessMessage.Ready, session, nonce));
        frame[offset] ^= 1;
        Assert.Throws<ReleaseValidationException>(() => ReadinessWire.Validate(frame, ReadinessMessage.Ready, session, nonce));
    }

    [Fact]
    public async Task BoundsFrameReadsRejectsTruncationAndPropagatesCancellation()
    {
        using var truncated = new MemoryStream(new byte[69]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ReadinessWire.ReadAsync(truncated, CancellationToken.None));
        using var full = new MemoryStream(new byte[10000]);
        Assert.Equal(70, (await ReadinessWire.ReadAsync(full, CancellationToken.None)).Length);
        Assert.Equal(70, full.Position);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadinessWire.ReadAsync(full, canceled.Token));
    }

    [Fact]
    public void OldSessionOrNonceCannotBeReplayed()
    {
        var session = Session();
        var nonce = RandomNumberGenerator.GetBytes(32);
        var frame = ReadinessWire.Encode(ReadinessMessage.Ready, session, nonce);
        Assert.Throws<ReleaseValidationException>(() => ReadinessWire.Validate(frame, ReadinessMessage.Ready, Session(), nonce));
        Assert.Throws<ReleaseValidationException>(() => ReadinessWire.Validate(frame, ReadinessMessage.Ready, session, RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<ReleaseValidationException>(() => ReadinessWire.Encode(ReadinessMessage.Ready, new(Guid.Empty, Guid.NewGuid()), nonce));
        Assert.Throws<ReleaseValidationException>(() => ReadinessWire.Encode((ReadinessMessage)99, session, nonce));
        Assert.Throws<ReleaseValidationException>(() => ReadinessWire.Encode(ReadinessMessage.Ready, session, new byte[1000]));
    }

    [Fact]
    public async Task SameUserPipeRequiresLivePidTimeAndPathAndSessionIsSingleUse()
    {
        using var process = Process.GetCurrentProcess();
        var identity = PipeProcessIdentity.Capture(process, process.MainModule!.FileName);
        Assert.Throws<ReleaseValidationException>(() => (identity with { CreationUtcTicks = identity.CreationUtcTicks + 1 }).ValidateLive());
        Assert.Throws<ReleaseValidationException>(() => (identity with { ExecutablePath = Path.Combine(Path.GetTempPath(), "not-the-peer.exe") }).ValidateLive());
        var session = Session();
        using var server = new ReadinessServer(session);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ReleaseValidationException>(() => server.AcceptAsync(identity, deadline.Token));
        await Assert.ThrowsAsync<ReleaseValidationException>(() => server.ConfirmReceiptAsync(identity, deadline.Token));
        var receive = server.ReceiveReadyAsync(identity, deadline.Token);
        using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(deadline.Token);
        identity.ValidatePipe(client, serverSide: false);
        var nonce = ReadinessWire.Validate(await ReadinessWire.ReadAsync(client, deadline.Token), ReadinessMessage.Challenge, session);
        await client.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Ready, session, nonce), deadline.Token);
        await receive;
        await server.AcceptAsync(identity, deadline.Token);
        ReadinessWire.Validate(await ReadinessWire.ReadAsync(client, deadline.Token), ReadinessMessage.Accepted, session, nonce);
        await client.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Receipt, session, nonce), deadline.Token);
        await server.ConfirmReceiptAsync(identity, deadline.Token);
        await Assert.ThrowsAsync<ReleaseValidationException>(() => server.ConfirmReceiptAsync(identity, deadline.Token));
        await Assert.ThrowsAsync<ReleaseValidationException>(() => server.ReceiveReadyAsync(identity, deadline.Token));
        await Assert.ThrowsAsync<ReleaseValidationException>(() => server.AcceptAsync(identity, deadline.Token));
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotAuthorizeAcceptanceOrReuse()
    {
        using var process = Process.GetCurrentProcess();
        var identity = PipeProcessIdentity.Capture(process, process.MainModule!.FileName);
        using var server = new ReadinessServer(Session());
        using var cancel = new CancellationTokenSource();
        var pending = server.ReceiveReadyAsync(identity, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAsync<ReleaseValidationException>(() => server.AcceptAsync(identity, CancellationToken.None));
    }

    [Fact]
    public async Task WrongPipePeerGetsNoChallenge()
    {
        using var process = Process.GetCurrentProcess();
        var identity = PipeProcessIdentity.Capture(process, process.MainModule!.FileName) with { ProcessId = int.MaxValue };
        using var server = new ReadinessServer(Session());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = server.ReceiveReadyAsync(identity, deadline.Token);
        using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(deadline.Token);
        await Assert.ThrowsAsync<ReleaseValidationException>(() => pending);
        using var noResponse = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.ReadExactlyAsync(new byte[1], noResponse.Token));
        await Assert.ThrowsAsync<ReleaseValidationException>(() => server.AcceptAsync(identity, deadline.Token));
    }

    [Fact]
    public void SignedReleaseContractsRemainByteCompatibleWithTheReflectionBaseline()
    {
        using var fixture = new UpdateDiscoveryFixture();
        fixture.Release();
        var manifest = fixture.Verifier.Verify(fixture.Handler.Assets[101], fixture.Handler.Assets[102]);
        Compare(manifest);
        Compare(new SignatureEnvelope(ManifestRules.Algorithm, "test-key", Convert.ToBase64String(new byte[64])));
        Compare(new ReleasePolicy(1, 19041, 1, new(1, 12, 12),
            [new("test-key", Convert.ToBase64String(fixture.Key.ExportSubjectPublicKeyInfo()))]));

        static void Compare<T>(T value) => Assert.Equal(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, ContractJson.Options), ContractJson.Write(value));
    }

    [Fact]
    public void SourceGeneratedRecoveryJsonPreservesStrictWireFormat()
    {
        using var f = new RecoveryFixture();
        var generated = RecoveryJson.Write(f.Record, RecoveryJson.Context.RecoveryJournal);
        Assert.Equal(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(f.Record, ContractJson.Options), generated);
        Assert.Equal(f.Record, RecoveryJson.Read(generated, RecoveryRules.JournalLimit, RecoveryJson.Context.RecoveryJournal));
        var missing = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        Assert.ThrowsAny<Exception>(() => RecoveryJson.Read(missing, 4096, RecoveryJson.Context.RecoveryJournal));
        Assert.Throws<ReleaseValidationException>(() => ContractJson.Write(new { Unregistered = true }));
    }
}
