using System.IO.Pipes;
using System.Security.Cryptography;

namespace PrivacyMask.Updates;

internal enum ReadinessMessage : byte { Challenge = 1, Ready = 2, Accepted = 3, Receipt = 4 }
internal sealed record ReadinessSession(Guid InstallId, Guid OperationId)
{
    internal void Validate()
    {
        if (InstallId == Guid.Empty || OperationId == Guid.Empty) throw new ReleaseValidationException("Invalid readiness session.");
    }
}

/// <summary>Fixed 70-byte frames; no JSON, paths, commands, settings, PINs or unbounded allocations.</summary>
internal static class ReadinessWire
{
    internal const int FrameSize = 70;
    internal static byte[] Encode(ReadinessMessage kind, ReadinessSession session, ReadOnlySpan<byte> challenge)
    {
        session.Validate();
        if (!Enum.IsDefined(kind) || challenge.Length != 32) throw new ReleaseValidationException("Invalid readiness frame.");
        var frame = new byte[FrameSize];
        "PMUP"u8.CopyTo(frame);
        frame[4] = 1;
        frame[5] = (byte)kind;
        session.InstallId.TryWriteBytes(frame.AsSpan(6, 16));
        session.OperationId.TryWriteBytes(frame.AsSpan(22, 16));
        challenge.CopyTo(frame.AsSpan(38));
        return frame;
    }

    internal static byte[] Validate(byte[] frame, ReadinessMessage expected, ReadinessSession session, byte[]? challenge = null)
    {
        session.Validate();
        if (frame.Length != FrameSize || !frame.AsSpan(0, 4).SequenceEqual("PMUP"u8) || frame[4] != 1 || frame[5] != (byte)expected ||
            new Guid(frame.AsSpan(6, 16)) != session.InstallId || new Guid(frame.AsSpan(22, 16)) != session.OperationId ||
            (challenge is not null && (challenge.Length != 32 || !CryptographicOperations.FixedTimeEquals(frame.AsSpan(38), challenge))))
            throw new ReleaseValidationException("Invalid readiness response.");
        return frame.AsSpan(38, 32).ToArray();
    }

    internal static async Task<byte[]> ReadAsync(Stream stream, CancellationToken ct)
    {
        var frame = new byte[FrameSize];
        await stream.ReadExactlyAsync(frame, ct).ConfigureAwait(false);
        return frame;
    }
}

/// <summary>Single-use, current-user endpoint. A Ready response is NOT an install commit.</summary>
internal sealed class ReadinessServer : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly ReadinessSession _session;
    private readonly byte[] _challenge = RandomNumberGenerator.GetBytes(32);
    private int _used;
    internal string PipeName { get; } = "PrivacyMask.UpdateProbe." + Guid.NewGuid().ToString("N");

    internal ReadinessServer(ReadinessSession session)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        session.Validate();
        _session = session;
        _pipe = new(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 4096, 4096);
    }

    internal async Task ReceiveReadyAsync(PipeProcessIdentity expectedChild, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _used, 1, 0) != 0) throw new ReleaseValidationException("Readiness session already consumed.");
        await _pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
        // Authenticate the connected process BEFORE disclosing the fresh challenge.
        expectedChild.ValidatePipe(_pipe, serverSide: true);
        await _pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Challenge, _session, _challenge), ct).ConfigureAwait(false);
        var response = await ReadinessWire.ReadAsync(_pipe, ct).ConfigureAwait(false);
        ReadinessWire.Validate(response, ReadinessMessage.Ready, _session, _challenge);
        expectedChild.ValidatePipe(_pipe, serverSide: true);
        Volatile.Write(ref _used, 2);
    }

    internal async Task AcceptAsync(PipeProcessIdentity expectedChild, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _used, 3, 2) != 2) throw new ReleaseValidationException("No completed readiness exchange.");
        expectedChild.ValidatePipe(_pipe, serverSide: true);
        await _pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Accepted, _session, _challenge), ct).ConfigureAwait(false);
    }

    internal async Task ConfirmReceiptAsync(PipeProcessIdentity expectedChild, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _used, 4, 3) != 3) throw new ReleaseValidationException("No commit receipt to confirm.");
        expectedChild.ValidatePipe(_pipe, serverSide: true);
        ReadinessWire.Validate(await ReadinessWire.ReadAsync(_pipe, ct).ConfigureAwait(false), ReadinessMessage.Receipt, _session, _challenge);
        expectedChild.ValidatePipe(_pipe, serverSide: true);
    }

    public void Dispose() { _pipe.Dispose(); CryptographicOperations.ZeroMemory(_challenge); }
}
