using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrivacyMask.Updates;

internal enum PreparationCheckpoint { Authenticated, FreshReleaseChecked, DirectoryCreated, FileCopied, BeforeJournal, Prepared }
internal sealed class UpdatePreparationException(string reason) : Exception(reason)
{
    internal string Reason { get; } = reason;
}

/// <summary>
/// Internal preparation only: never replaces, executes or stops an app. The future coordinator MUST
/// freeze settings writers and serialize consent/Panic/edits through handoff; a Prepared journal is not apply authority.
/// </summary>
internal sealed class RecoveryPreparer
{
    private readonly ManagedInstallLease _lease;
    private readonly UpdateManifestVerifier _verifier;
    private readonly GitHubReleaseClient _discovery;
    private readonly TimeProvider _time;
    private readonly Func<string, long>? _availableSpace;
    private readonly Action<string, string, string?> _inspect;
    private readonly Action<PreparationCheckpoint, string>? _checkpoint;
    private int _busy;

    internal RecoveryPreparer(ManagedInstallLease lease, UpdateManifestVerifier verifier, GitHubReleaseClient discovery)
        : this(lease, verifier, discovery, TimeProvider.System) { }

    // Test-only seams, unavailable to the App (which still has no Updates reference).
    internal RecoveryPreparer(ManagedInstallLease lease, UpdateManifestVerifier verifier, GitHubReleaseClient discovery,
        TimeProvider time, Func<string, long>? availableSpace = null, Action<string, string, string?>? inspect = null,
        Action<PreparationCheckpoint, string>? checkpoint = null)
    {
        _lease = lease; _verifier = verifier; _discovery = discovery; _time = time;
        _availableSpace = availableSpace; _inspect = inspect ?? InspectExecutable; _checkpoint = checkpoint;
    }

    internal async Task<RecoveryJournal> PrepareAsync(string operationId, UpdateCheckContext context, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new UpdatePreparationException("preparation-in-progress");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var ct = linked.Token;
        try
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            ct.ThrowIfCancellationRequested();
            _lease.AssertHeld();
            if (!RecoveryRules.IsId(operationId) || UpdateEligibility.CheckContext(context) is not null ||
                context.UpdaterProtocol != 1 || context.WindowsBuild != Environment.OSVersion.Version.Build ||
                RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new ReleaseValidationException("Unsupported preparation context.");
            var store = new RecoveryJournalStore(_lease);
            if (store.Read() is not null) throw new UpdatePreparationException("existing-recovery-operation");

            // Fixed descendants of the same private managed root: no caller-supplied staging/app paths.
            var stageRoot = Path.Combine(_lease.Root, "update-staging");
            var stage = Path.Combine(stageRoot, "operation-" + operationId);
            ManagedInstallLease.AssertPrivate(stageRoot);
            ManagedInstallLease.AssertPrivate(stage);
            AssertContents(stage, ["package.zip", "PrivacyMask.App.exe", "update-manifest.json", "update-manifest.sig.json"]);
            using var metadata = await PreparationFile.OpenAsync(Path.Combine(stage, "update-manifest.json"), ManifestRules.ManifestLimit, true, ct).ConfigureAwait(false);
            using var signature = await PreparationFile.OpenAsync(Path.Combine(stage, "update-manifest.sig.json"), ManifestRules.SignatureLimit, true, ct).ConfigureAwait(false);
            var manifestBytes = metadata.ReadMetadata(ManifestRules.ManifestLimit);
            var signatureBytes = signature.ReadMetadata(ManifestRules.SignatureLimit);
            var manifest = _verifier.Verify(manifestBytes, signatureBytes);
            if (!UpdateEligibility.IsNewerForChannel(manifest.Version, manifest.Channel == "preview", context) ||
                !UpdateEligibility.IsCompatible(manifest, context)) throw new ReleaseValidationException("Ineligible preparation target.");
            using var archive = await PreparationFile.OpenAsync(Path.Combine(stage, "package.zip"), ManifestRules.MaximumFileSize, true, ct).ConfigureAwait(false);
            using var target = await PreparationFile.OpenAsync(Path.Combine(stage, "PrivacyMask.App.exe"), ManifestRules.MaximumFileSize, true, ct).ConfigureAwait(false);
            if (archive.Snapshot != new RecoveryFile(true, manifest.Package.Size, manifest.Package.Sha256) ||
                target.Snapshot != new RecoveryFile(true, manifest.Files[0].Size, manifest.Files[0].Sha256))
                throw new ReleaseValidationException("Staged package or payload changed.");
            _inspect(Path.Combine(stage, "PrivacyMask.App.exe"), manifest.Version, manifest.CommitSha);
            _checkpoint?.Invoke(PreparationCheckpoint.Authenticated, "stage");

            // An in-memory timestamp or a locally edited journal is not a discovery receipt.
            // Always perform fixed-repository, anonymous discovery again, even for a previously staged package.
            var checkedAt = _time.GetUtcNow();
            var checkStarted = _time.GetTimestamp();
            var fresh = await _discovery.CheckAsync(context, ct).ConfigureAwait(false);
            if (fresh.Status != UpdateCheckStatus.Available || fresh.Candidate is not { } selected ||
                !selected.ManifestBytes.Span.SequenceEqual(manifestBytes) || !selected.SignatureBytes.Span.SequenceEqual(signatureBytes))
                throw new UpdatePreparationException("fresh-release-check-required");
            _checkpoint?.Invoke(PreparationCheckpoint.FreshReleaseChecked, "release");
            AssertFresh();
            _lease.AssertHeld();
            if (store.Read() is not null) throw new UpdatePreparationException("existing-recovery-operation");

            var appDirectory = Path.Combine(_lease.Root, "app");
            ManagedInstallLease.AssertPrivate(appDirectory);
            AssertContents(appDirectory, ["PrivacyMask.App.exe"]);
            var appPath = Path.Combine(appDirectory, "PrivacyMask.App.exe");
            using var original = await PreparationFile.OpenAsync(appPath, ManifestRules.MaximumFileSize, true, ct).ConfigureAwait(false);
            _inspect(appPath, context.InstalledVersion, null);
            using var primary = await PreparationFile.OpenAsync(Path.Combine(_lease.Root, "settings.v1.json"), RecoveryRules.SettingsLimit, false, ct).ConfigureAwait(false);
            using var secondary = await PreparationFile.OpenAsync(Path.Combine(_lease.Root, "settings.v1.json.bak"), RecoveryRules.SettingsLimit, false, ct).ConfigureAwait(false);
            AssertSpace(copiesCreated: false);
            var operations = Path.Combine(_lease.Root, "update-operations");
            if (!Path.Exists(operations)) ManagedInstallLease.CreatePrivateDirectory(operations);
            ManagedInstallLease.AssertPrivate(operations);
            var operation = Path.Combine(operations, "operation-" + operationId);
            ManagedInstallLease.CreatePrivateDirectory(operation);
            _checkpoint?.Invoke(PreparationCheckpoint.DirectoryCreated, "operation");
            await Copy(original, "original-app.exe").ConfigureAwait(false);
            await Copy(primary, "original-settings.json").ConfigureAwait(false);
            await Copy(secondary, "original-settings.bak").ConfigureAwait(false);
            await Copy(target, "target-app.exe").ConfigureAwait(false);
            await Copy(metadata, "update-manifest.json").ConfigureAwait(false);
            await Copy(signature, "update-manifest.sig.json").ConfigureAwait(false);
            AssertSpace(copiesCreated: true);
            var originalVersion = context.InstalledVersion.Split('+')[0];
            var journal = new RecoveryJournal(1, _lease.InstallId, operationId, 1, RecoveryStep.Prepared,
                originalVersion, manifest.Version, original.Snapshot, target.Snapshot, primary.Snapshot, secondary.Snapshot,
                metadata.Snapshot.Sha256!, signature.Snapshot.Sha256!);
            _checkpoint?.Invoke(PreparationCheckpoint.BeforeJournal, "journal");
            // Never publish Prepared over damaged/missing copies, a stale release, cancellation or changed lease.
            foreach (var (name, expected, maximum) in new[] {
                ("original-app.exe", original.Snapshot, ManifestRules.MaximumFileSize),
                ("original-settings.json", primary.Snapshot, RecoveryRules.SettingsLimit),
                ("original-settings.bak", secondary.Snapshot, RecoveryRules.SettingsLimit),
                ("target-app.exe", target.Snapshot, ManifestRules.MaximumFileSize),
                ("update-manifest.json", metadata.Snapshot, (long)ManifestRules.ManifestLimit),
                ("update-manifest.sig.json", signature.Snapshot, (long)ManifestRules.SignatureLimit) })
                if (RecoveryRestorer.Inspect(Path.Combine(operation, name), maximum) != expected)
                    throw new ReleaseValidationException("Preparation copy changed.");
            // A previously absent settings file has no open handle; detect a writer creating it meanwhile.
            if (RecoveryRestorer.Inspect(appPath, ManifestRules.MaximumFileSize) != original.Snapshot ||
                RecoveryRestorer.Inspect(Path.Combine(_lease.Root, "settings.v1.json"), RecoveryRules.SettingsLimit) != primary.Snapshot ||
                RecoveryRestorer.Inspect(Path.Combine(_lease.Root, "settings.v1.json.bak"), RecoveryRules.SettingsLimit) != secondary.Snapshot)
                throw new ReleaseValidationException("Saved baseline changed during preparation.");
            AssertFresh();
            ct.ThrowIfCancellationRequested();
            _lease.AssertHeld();
            store.Write(journal);
            _checkpoint?.Invoke(PreparationCheckpoint.Prepared, "journal");
            // No cancellation after promotion: the durable journal is now authoritative.
            return journal;

            async Task Copy(PreparationFile source, string name)
            {
                ct.ThrowIfCancellationRequested();
                _lease.AssertHeld();
                _ = _lease.OperationDirectory(operationId);
                await source.CopyToAsync(Path.Combine(operation, name), ct).ConfigureAwait(false);
                _checkpoint?.Invoke(PreparationCheckpoint.FileCopied, name);
            }
            void AssertFresh()
            {
                var age = _time.GetUtcNow() - checkedAt;
                var elapsed = _time.GetElapsedTime(checkStarted);
                if (age < TimeSpan.Zero || elapsed < TimeSpan.Zero || age > TimeSpan.FromHours(24) || elapsed > TimeSpan.FromHours(24))
                    throw new UpdatePreparationException("fresh-release-check-required");
            }
            void AssertSpace(bool copiesCreated)
            {
                var required = PreparationBudget.Required(original.Snapshot, primary.Snapshot, secondary.Snapshot, target.Snapshot, copiesCreated);
                var free = _availableSpace?.Invoke(_lease.Root) ?? new DriveInfo(Path.GetPathRoot(_lease.Root)!).AvailableFreeSpace;
                if (free < required) throw new UpdatePreparationException("insufficient-recovery-space");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException("Update preparation canceled.", cancellationToken); }
        catch (OperationCanceledException) { throw new UpdatePreparationException("preparation-timeout"); }
        catch (Exception ex) when (ex is ReleaseValidationException or JsonException or DecoderFallbackException or
            CryptographicException or FormatException or BadImageFormatException or InvalidDataException or OverflowException)
        { throw new UpdatePreparationException("preparation-validation-failed"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { throw new UpdatePreparationException("preparation-io-failed"); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private static void AssertContents(string directory, string[] expected)
    {
        var names = Directory.EnumerateFileSystemEntries(directory).Take(expected.Length + 1).Select(Path.GetFileName).ToArray();
        if (names.Length != expected.Length || names.Any(name => !expected.Contains(name, StringComparer.Ordinal)))
            throw new ReleaseValidationException("Unexpected managed directory content.");
    }

    private static void InspectExecutable(string path, string version, string? commit)
    {
        if (commit is not null) { UpdatePayload.InspectExecutable(path, version, commit); return; }
        var product = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
        var parts = product.Split('+');
        if (parts.Length != 2 || !ManifestRules.IsCommit(parts[1]) ||
            !SemanticVersion.TryParse(version, out var installed) || installed.ProductVersion != parts[0] ||
            (version.Contains('+') && version != product)) throw new ReleaseValidationException("Installed executable identity changed.");
        UpdatePayload.InspectExecutable(path, parts[0], parts[1]);
    }
}
