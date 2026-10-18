using System.Diagnostics;

namespace PrivacyMask.Updates;

internal enum LauncherApplyOutcome { Committed, PreviousVersionRestarted }

/// <summary>Installed worker. Authenticates its App before fresh discovery and requires exact clean exit before replacement.</summary>
internal static class LauncherApply
{
    internal static async Task<LauncherApplyOutcome> RunAsync(string launcherPath, IReadOnlyList<string> arguments,
        UpdateManifestVerifier verifier, GitHubReleaseClient discovery, CancellationToken cancellationToken = default,
        string? expectedRoot = null, Func<ProcessStartInfo, Process?>? restart = null,
        TransactionTestHooks? hooks = null, Action? applicationExited = null)
    {
        var request = UpdateApplyRequest.Parse(arguments);
        if (!verifier.IsConfigured) throw new ReleaseValidationException("Update trust roots are not configured.");
        var root = Path.GetFullPath(expectedRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ManifestRules.AppId));
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(launcherPath) ||
            !string.Equals(Path.GetFullPath(launcherPath), Path.Combine(root, LauncherBoot.LauncherFileName), StringComparison.OrdinalIgnoreCase))
            throw new ReleaseValidationException("Update worker is outside the managed installation.");
        ManagedInstallLease.AssertPrivate(launcherPath);
        using var lease = ManagedInstallLease.Open(root);
        if (request.InstallId != lease.InstallId) throw new ReleaseValidationException("Update install identity changed.");
        var record = new RecoveryJournalStore(lease).Read();
        if (record is null || record.OperationId != request.OperationId || record.Step != RecoveryStep.Prepared)
            throw new ReleaseValidationException("Missing exact prepared operation.");
        var app = Path.Combine(root, "app", LauncherBoot.ApplicationFileName);
        var version = FileVersionInfo.GetVersionInfo(app).ProductVersion
            ?? throw new ReleaseValidationException("Missing installed version.");
        // Channel authorizes only the already prepared exact target, not selection of a different release.
        var context = new UpdateCheckContext(version, record.TargetVersion.Contains('-') ? UpdateChannel.Preview : UpdateChannel.Stable,
            "win-x64", Environment.OSVersion.Version.Build, 1, 12, InstallationKind.Managed);
        using var exit = AppProcessExit.Capture(lease, request.OperationId, request.ApplicationProcessId,
            request.ApplicationCreationUtcTicks);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var identity = new PipeProcessIdentity(request.ApplicationProcessId, request.ApplicationCreationUtcTicks, app);
        using var client = await UpdateHandoffClient.ConnectAsync(request.PipeName,
            new(Guid.ParseExact(lease.InstallId, "N"), Guid.ParseExact(request.OperationId, "N")), identity, deadline.Token).ConfigureAwait(false);
        // All expensive rejection paths run while the old App is still protecting windows.
        using (var inputs = await ForwardUpdateInputs.VerifyAsync(lease, request.OperationId, context, verifier,
            discovery, TimeProvider.System, deadline.Token).ConfigureAwait(false))
        {
            inputs.AssertFresh();
            inputs.AssertCurrentBaseline();
            if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < PreparationBudget.Required(
                record.OriginalApp, record.SettingsPrimary, record.SettingsBackup, record.TargetApp, true))
                throw new ReleaseValidationException("Insufficient update recovery space.");
        }
        await client.RequestExitAsync(deadline.Token).ConfigureAwait(false);
        await exit.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
        applicationExited?.Invoke();
        // User cancellation ends at the authenticated handoff. Existing transaction deadlines remain.
        var result = await new UpdateTransaction(lease, verifier, discovery, TimeProvider.System, hooks: hooks).ApplyAsync(request.OperationId,
            context, exit, request.Mode == UpdateStartMode.Minimized, CancellationToken.None).ConfigureAwait(false);
        if (result.Outcome is TransactionOutcome.Committed or TransactionOutcome.CommittedReceiptLost)
            return LauncherApplyOutcome.Committed;
        if (result.Outcome == TransactionOutcome.Rejected)
        {
            if (new RecoveryRestorer(lease).Recover().Outcome != RecoveryOutcome.Recovered)
                throw new ReleaseValidationException("Update recovery could not complete.");
        }
        else if (result.Outcome != TransactionOutcome.RecoveredFiles)
            throw new ReleaseValidationException("Update recovery requires repair.");
        new RecoveryJournalStore(lease).ArchiveTerminal(request.OperationId);
        _ = await new UpdateRetention(lease).RunAsync().ConfigureAwait(false);
        using var previous = LauncherBoot.StartVerifiedApplication(lease, request.Mode == UpdateStartMode.Minimized, restart);
        return LauncherApplyOutcome.PreviousVersionRestarted;
    }
}
