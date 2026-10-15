using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;

namespace PrivacyMask.Updates;

/// <summary>Launcher-side peer for one App-authorized graceful-exit handoff.</summary>
internal sealed class UpdateHandoffClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly Process _application;
    private readonly PipeProcessIdentity _applicationIdentity;
    private readonly HandoffSession _session;
    private byte[]? _challenge;
    private int _used;

    private UpdateHandoffClient(NamedPipeClientStream pipe, Process application,
        PipeProcessIdentity applicationIdentity, HandoffSession session)
    {
        _pipe = pipe;
        _application = application;
        _applicationIdentity = applicationIdentity;
        _session = session;
    }

    internal static async Task<UpdateHandoffClient> ConnectAsync(string pipeName,
        HandoffSession session, PipeProcessIdentity expectedApplication,
        CancellationToken cancellationToken)
    {
        session.Validate();
        if (!pipeName.StartsWith(UpdateHandoffServer.PipePrefix, StringComparison.Ordinal) ||
            !RecoveryRules.IsId(pipeName[UpdateHandoffServer.PipePrefix.Length..]))
            throw new ReleaseValidationException("Invalid update handoff pipe.");

        Process? application = null;
        NamedPipeClientStream? pipe = null;
        try
        {
            application = Process.GetProcessById(expectedApplication.ProcessId);
            _ = application.SafeHandle;
            expectedApplication.ValidateLive();
            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            expectedApplication.ValidatePipe(pipe, serverSide: false);
            return new(pipe, application, expectedApplication, session);
        }
        catch
        {
            pipe?.Dispose();
            application?.Dispose();
            throw;
        }
    }

    internal async Task RequestExitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _used, 1, 0) != 0)
            throw new ReleaseValidationException("Update handoff request already consumed.");
        _applicationIdentity.ValidatePipe(_pipe, serverSide: false);
        var challenge = HandoffWire.Validate(
            await HandoffWire.ReadAsync(_pipe, cancellationToken).ConfigureAwait(false),
            HandoffMessage.Challenge, _session);
        _challenge = challenge;
        await _pipe.WriteAsync(HandoffWire.Encode(HandoffMessage.ExitRequest, _session, challenge),
            cancellationToken).ConfigureAwait(false);
        _applicationIdentity.ValidatePipe(_pipe, serverSide: false);
        HandoffWire.Validate(await HandoffWire.ReadAsync(_pipe, cancellationToken).ConfigureAwait(false),
            HandoffMessage.Accepted, _session, challenge);
        _applicationIdentity.ValidatePipe(_pipe, serverSide: false);
        Volatile.Write(ref _used, 2);
    }

    public void Dispose()
    {
        _pipe.Dispose();
        _application.Dispose();
        if (_challenge is not null) CryptographicOperations.ZeroMemory(_challenge);
    }
}
