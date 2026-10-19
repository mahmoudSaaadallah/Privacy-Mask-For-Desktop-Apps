using System.Diagnostics;
using System.IO;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

/// <summary>Called only with frozen shell writers and revalidated restart safety under the required update policy.</summary>
internal static class AuthenticatedUpdateHandoff
{
    internal static async Task RunAsync(string operationId, UpdateChannel channel, bool minimized,
        string settingsPath, Action assertReady, UpdateManifestVerifier verifier,
        GitHubReleaseClient discovery, CancellationToken cancellationToken)
    {
        if (!verifier.IsConfigured || !RecoveryRules.IsId(operationId) || !Enum.IsDefined(channel))
            throw new InvalidOperationException("Signed in-app updates are not configured.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ManifestRules.AppId);
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Missing application path.");
        var launcher = ManagedStartupPath.Resolve(self);
        if (launcher is null || !string.Equals(Path.GetFullPath(self), Path.Combine(root, "app", LauncherBoot.ApplicationFileName), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(settingsPath), Path.Combine(root, "settings.v1.json"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run the managed installer before using in-app updates.");
        var context = new UpdateCheckContext(FileVersionInfo.GetVersionInfo(self).ProductVersion!, channel,
            "win-x64", Environment.OSVersion.Version.Build, 1, AppSettings.CurrentVersion, InstallationKind.Managed);
        Process? worker = null;
        UpdateHandoffServer? server = null;
        var completed = false;
        var ownsPreparation = false;
        string? installId = null;
        try
        {
            assertReady();
            using (var lease = ManagedInstallLease.Open(root))
            {
                var store = new RecoveryJournalStore(lease);
                var existing = store.Read();
                if (existing is { Step: RecoveryStep.Committed or RecoveryStep.Recovered }) store.ArchiveTerminal(existing.OperationId);
                else if (existing is not null) throw new InvalidOperationException("A previous update needs recovery before another update can start.");
                _ = await new UpdateRetention(lease).RunAsync(operationId, cancellationToken);
                installId = lease.InstallId;
                ownsPreparation = true; // Covers loss of the response after a durable Prepared promotion.
                await new RecoveryPreparer(lease, verifier, discovery).PrepareAsync(operationId, context, cancellationToken);
                assertReady();
                server = new(new(Guid.ParseExact(installId, "N"), Guid.ParseExact(operationId, "N")));
            }
            // The worker must independently acquire the install lease. Writers stay frozen across this transfer.
            using var launcherPin = await PreparationFile.OpenAsync(launcher, ManifestRules.MaximumFileSize, true, cancellationToken);
            using var appProcess = Process.GetCurrentProcess();
            var identity = PipeProcessIdentity.Capture(appProcess, self);
            var request = new UpdateApplyRequest(server.PipeName, installId, operationId,
                identity.ProcessId, identity.CreationUtcTicks, minimized ? UpdateStartMode.Minimized : UpdateStartMode.Normal);
            var start = new ProcessStartInfo(launcher) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            foreach (var argument in request.ToArguments()) start.ArgumentList.Add(argument);
            worker = Process.Start(start) ?? throw new IOException("The update worker did not start.");
            var workerIdentity = PipeProcessIdentity.Capture(worker, launcher);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var monitor = MonitorWorkerAsync();
            try
            {
                await server.ReceiveExitRequestAsync(workerIdentity, deadline.Token);
                assertReady();
                await server.AcceptAsync(deadline.Token);
                // No user cancellation after the final acknowledgement: next step is awaited App shutdown.
                completed = true;
            }
            finally { deadline.Cancel(); await monitor; }

            async Task MonitorWorkerAsync()
            {
                try { await worker.WaitForExitAsync(deadline.Token); deadline.Cancel(); }
                catch (OperationCanceledException) { }
            }
        }
        finally
        {
            server?.Dispose();
            if (!completed)
            {
                try
                {
                    if (worker is not null)
                    {
                        // Only our retained exact worker; never the application, name or process tree.
                        if (!worker.HasExited) worker.Kill(entireProcessTree: false);
                        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await worker.WaitForExitAsync(stop.Token);
                    }
                    if (ownsPreparation)
                    {
                        using var lease = ManagedInstallLease.Open(root);
                        var store = new RecoveryJournalStore(lease);
                        var record = store.Read();
                        if (record is not null)
                        {
                            if (lease.InstallId != installId || record.OperationId != operationId)
                                throw new InvalidOperationException("Update identity changed during cancellation.");
                            store.AbandonPrepared(operationId);
                            _ = await new UpdateRetention(lease).RunAsync(operationId);
                        }
                    }
                }
                catch (Exception) { throw new UnsafeUpdateResumeException(); }
                finally { worker?.Dispose(); }
            }
            else worker?.Dispose(); // Worker continues under its own lease while the App shuts down.
        }
    }
}
