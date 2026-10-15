using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;

namespace PrivacyMask.Updates;

internal sealed class UpdateReadinessClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly Process _parent;
    private readonly PipeProcessIdentity _parentIdentity;
    private readonly ReadinessSession _session;
    private readonly UpdateStartLayout _layout;
    private byte[]? _challenge;
    private int _state;

    private UpdateReadinessClient(NamedPipeClientStream pipe, Process parent, PipeProcessIdentity parentIdentity,
        ReadinessSession session, UpdateStartLayout layout)
    {
        _pipe = pipe;
        _parent = parent;
        _parentIdentity = parentIdentity;
        _session = session;
        _layout = layout;
    }

    internal static async Task<UpdateReadinessClient> ConnectAsync(UpdateStartRequest request,
        UpdateStartLayout layout, CancellationToken cancellationToken)
    {
        if (layout.Journal.InstallId != request.InstallId || layout.Journal.OperationId != request.OperationId ||
            layout.Journal.Step != RecoveryStep.AwaitingReadiness)
            throw new ReleaseValidationException("Update readiness identity mismatch.");

        Process? parent = null;
        NamedPipeClientStream? pipe = null;
        try
        {
            parent = Process.GetProcessById(request.ParentProcessId);
            _ = parent.SafeHandle;
            var identity = new PipeProcessIdentity(request.ParentProcessId, request.ParentCreationUtcTicks, layout.LauncherPath);
            identity.ValidateLive();
            var session = new ReadinessSession(Guid.ParseExact(request.InstallId, "N"), Guid.ParseExact(request.OperationId, "N"));
            pipe = new NamedPipeClientStream(".", request.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            identity.ValidatePipe(pipe, serverSide: false);
            return new(pipe, parent, identity, session, layout);
        }
        catch
        {
            pipe?.Dispose();
            parent?.Dispose();
            throw;
        }
    }

    internal async Task SignalReadyAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new ReleaseValidationException("Update readiness was already signaled.");
        _parentIdentity.ValidatePipe(_pipe, serverSide: false);
        _layout.AssertApplicationUnchanged();
        if (_layout.ReadCurrentJournal() != _layout.Journal)
            throw new ReleaseValidationException("Update transaction changed before readiness.");
        var challenge = ReadinessWire.Validate(
            await ReadinessWire.ReadAsync(_pipe, cancellationToken).ConfigureAwait(false),
            ReadinessMessage.Challenge, _session);
        _challenge = challenge;
        await _pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Ready, _session, challenge), cancellationToken)
            .ConfigureAwait(false);
        _parentIdentity.ValidatePipe(_pipe, serverSide: false);
        Volatile.Write(ref _state, 2);
    }

    internal async Task ConfirmCommittedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 3, 2) != 2 || _challenge is null)
            throw new ReleaseValidationException("Update readiness has not completed.");
        _parentIdentity.ValidatePipe(_pipe, serverSide: false);
        ReadinessWire.Validate(await ReadinessWire.ReadAsync(_pipe, cancellationToken).ConfigureAwait(false),
            ReadinessMessage.Accepted, _session, _challenge);
        _parentIdentity.ValidatePipe(_pipe, serverSide: false);
        _layout.AssertApplicationUnchanged();
        var expected = _layout.Journal with
        {
            Step = RecoveryStep.Committed,
            Revision = _layout.Journal.Revision + 1
        };
        if (_layout.ReadCurrentJournal() != expected)
            throw new ReleaseValidationException("Durable update commit was not confirmed.");
        await _pipe.WriteAsync(ReadinessWire.Encode(ReadinessMessage.Receipt, _session, _challenge), cancellationToken)
            .ConfigureAwait(false);
        _parentIdentity.ValidatePipe(_pipe, serverSide: false);
        Volatile.Write(ref _state, 4);
    }

    public void Dispose()
    {
        _pipe.Dispose();
        _parent.Dispose();
        if (_challenge is not null) CryptographicOperations.ZeroMemory(_challenge);
    }
}
