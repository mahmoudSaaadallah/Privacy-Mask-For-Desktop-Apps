using System.IO;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal sealed record UpdateDownloadActions(
    Func<UpdateCandidate, UpdateReleaseChannel, IProgress<UpdateDownloadProgress>, CancellationToken, Task> Prepare,
    Func<CancellationToken, Task<bool>> Discard);

/// <summary>Managed staging only. Never prepares a journal, starts a worker or changes installed files.</summary>
internal sealed class RuntimeUpdateDownload(RuntimeUpdateDiscovery discovery)
{
    private StagedUpdate? _staged;

    internal Task PrepareAsync(UpdateCandidate candidate, UpdateReleaseChannel channel,
        IProgress<UpdateDownloadProgress> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (discovery.Preflight(channel) is not null) throw new UpdateDownloadException("update-unavailable");
        return Task.Run(async () =>
        {
            try
            {
            if (_staged is not null) throw new UpdateDownloadException("download-in-progress");
            // Preflight again immediately before taking the lease; source/custom/portable roots cannot stage.
            if (discovery.Preflight(channel) is not null) throw new UpdateDownloadException("update-unavailable");
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ManifestRules.AppId);
            using var lease = ManagedInstallLease.Open(root);
            var journals = new RecoveryJournalStore(lease);
            if (journals.Read() is { Step: RecoveryStep.Committed or RecoveryStep.Recovered } terminal)
                journals.ArchiveTerminal(terminal.OperationId);
            using var stager = new UpdateStager(UpdateRuntimePolicy.CreateVerifier());
            _staged = await stager.PrepareManagedAsync(candidate, RuntimeUpdateDiscovery.Context(channel), lease, progress, token);
            // A completed handle is retained even if cancellation raced return: it is verified, never installed.
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ReleaseValidationException)
            {
                // Stager failures already carry cleanup/retry metadata. These are pre-stager managed-layout failures.
                throw new UpdateDownloadException("managed-staging-unavailable");
            }
        }, token);
    }

    internal Task<bool> DiscardAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (_staged is null) return true;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ManifestRules.AppId);
        using var lease = ManagedInstallLease.Open(root);
        if (new RecoveryJournalStore(lease).Read() is not null) return false;
        lease.AssertHeld();
        if (!_staged.TryDiscard()) return false;
        _staged = null;
        return true;
    }, token);
}
