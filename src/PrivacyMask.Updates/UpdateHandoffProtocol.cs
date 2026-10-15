using System.IO.Pipes;
using System.Security.Cryptography;

namespace PrivacyMask.Updates;

internal enum HandoffMessage : byte { Challenge = 1, ExitRequest = 2, Accepted = 3, Receipt = 4, Complete = 5 }

internal sealed record HandoffSession(Guid InstallId, Guid OperationId)
{
    internal void Validate()
    {
        if (InstallId == Guid.Empty || OperationId == Guid.Empty)
            throw new ReleaseValidationException("Invalid update handoff session.");
    }
}

/// <summary>Fixed 70-byte frames; no paths, commands, settings, PINs or variable allocations.</summary>
internal static class HandoffWire
{
    internal const int FrameSize = 70;

    internal static byte[] Encode(HandoffMessage kind, HandoffSession session, ReadOnlySpan<byte> challenge)
    {
        session.Validate();
        if (!Enum.IsDefined(kind) || challenge.Length != 32)
            throw new ReleaseValidationException("Invalid update handoff frame.");
        var frame = new byte[FrameSize];
        "PMUH"u8.CopyTo(frame);
        frame[4] = 1;
        frame[5] = (byte)kind;
        session.InstallId.TryWriteBytes(frame.AsSpan(6, 16));
        session.OperationId.TryWriteBytes(frame.AsSpan(22, 16));
        challenge.CopyTo(frame.AsSpan(38));
        return frame;
    }

    internal static byte[] Validate(byte[] frame, HandoffMessage expected, HandoffSession session,
        byte[]? challenge = null)
    {
        session.Validate();
        if (frame.Length != FrameSize || !frame.AsSpan(0, 4).SequenceEqual("PMUH"u8) ||
            frame[4] != 1 || frame[5] != (byte)expected ||
            new Guid(frame.AsSpan(6, 16)) != session.InstallId ||
            new Guid(frame.AsSpan(22, 16)) != session.OperationId ||
            (challenge is not null && (challenge.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(frame.AsSpan(38), challenge))))
            throw new ReleaseValidationException("Invalid update handoff response.");
        return frame.AsSpan(38, 32).ToArray();
    }

    internal static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var frame = new byte[FrameSize];
        await stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
        return frame;
    }
}

/// <summary>
/// App-owned single-use endpoint. Receiving a request only authenticates the worker;
/// AcceptAsync is called after the App has atomically armed its graceful shutdown.
/// </summary>
internal sealed class UpdateHandoffServer : IDisposable
{
    internal const string PipePrefix = "PrivacyMask.UpdateHandoff.";

    private readonly NamedPipeServerStream _pipe;
    private readonly HandoffSession _session;
    private readonly byte[] _challenge = RandomNumberGenerator.GetBytes(32);
    private PipeProcessIdentity? _worker;
    private int _state;

    internal string PipeName { get; } = PipePrefix + Guid.NewGuid().ToString("N");

    internal UpdateHandoffServer(HandoffSession session)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        session.Validate();
        _session = session;
        _pipe = new(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance,
            4096, 4096);
    }

    internal async Task ReceiveExitRequestAsync(PipeProcessIdentity expectedWorker,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new ReleaseValidationException("Update handoff session already consumed.");
        await _pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        expectedWorker.ValidatePipe(_pipe, serverSide: true);
        await _pipe.WriteAsync(HandoffWire.Encode(HandoffMessage.Challenge, _session, _challenge),
            cancellationToken).ConfigureAwait(false);
        HandoffWire.Validate(await HandoffWire.ReadAsync(_pipe, cancellationToken).ConfigureAwait(false),
            HandoffMessage.ExitRequest, _session, _challenge);
        expectedWorker.ValidatePipe(_pipe, serverSide: true);
        _worker = expectedWorker;
        Volatile.Write(ref _state, 2);
    }

    internal async Task AcceptAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 3, 2) != 2 || _worker is null)
            throw new ReleaseValidationException("No authenticated update handoff request.");
        _worker.ValidatePipe(_pipe, serverSide: true);
        await _pipe.WriteAsync(HandoffWire.Encode(HandoffMessage.Accepted, _session, _challenge),
            cancellationToken).ConfigureAwait(false);
        HandoffWire.Validate(await HandoffWire.ReadAsync(_pipe, cancellationToken).ConfigureAwait(false),
            HandoffMessage.Receipt, _session, _challenge);
        _worker.ValidatePipe(_pipe, serverSide: true);
        await _pipe.WriteAsync(HandoffWire.Encode(HandoffMessage.Complete, _session, _challenge),
            cancellationToken).ConfigureAwait(false);
        _worker.ValidatePipe(_pipe, serverSide: true);
    }

    public void Dispose()
    {
        _pipe.Dispose();
        CryptographicOperations.ZeroMemory(_challenge);
    }
}
