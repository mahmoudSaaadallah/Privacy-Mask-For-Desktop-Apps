using System.Runtime.InteropServices;

namespace PrivacyMask.Updates;

/// <summary>Independent forward revalidation; a journal alone never authenticates the target or authorizes launch.</summary>
internal sealed class ForwardUpdateInputs : IDisposable
{
    private readonly List<PreparationFile> _pins = [];
    private readonly ManagedInstallLease _lease;
    private readonly TimeProvider _time;
    private DateTimeOffset _checkedAt;
    private long _checkTimestamp;
    internal RecoveryJournal Record { get; private set; } = null!;
    internal UpdateManifest Manifest { get; private set; } = null!;
    internal PreparationFile Target { get; private set; } = null!;
    internal string Operation { get; private set; } = "";

    private ForwardUpdateInputs(ManagedInstallLease lease, TimeProvider time) { _lease = lease; _time = time; }

    internal static async Task<ForwardUpdateInputs> VerifyAsync(ManagedInstallLease lease, string operationId,
        UpdateCheckContext context, UpdateManifestVerifier verifier, GitHubReleaseClient discovery, TimeProvider time,
        CancellationToken ct, Action<string, string, string?>? inspect = null)
    {
        var inputs = new ForwardUpdateInputs(lease, time);
        try
        {
            inspect ??= RecoveryPreparer.InspectExecutable;
            lease.AssertHeld();
            if (!RecoveryRules.IsId(operationId) || UpdateEligibility.CheckContext(context) is not null || context.UpdaterProtocol != 1 ||
                context.WindowsBuild != Environment.OSVersion.Version.Build || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new ReleaseValidationException("Unsupported forward context.");
            var record = new RecoveryJournalStore(lease).Read();
            if (record is null || record.OperationId != operationId || record.Step != RecoveryStep.Prepared ||
                context.InstalledVersion.Split('+')[0] != record.OriginalVersion)
                throw new ReleaseValidationException("Forward update requires the exact prepared operation.");
            inputs.Record = record;
            inputs.Operation = lease.OperationDirectory(operationId);
            var names = new List<string> { "original-app.exe", "target-app.exe", "update-manifest.json", "update-manifest.sig.json" };
            if (record.SettingsPrimary.Exists) names.Add("original-settings.json");
            if (record.SettingsBackup.Exists) names.Add("original-settings.bak");
            if (File.Exists(Path.Combine(inputs.Operation, RecoveryJournalStore.UnpromotedFileName)))
            {
                names.Add(RecoveryJournalStore.UnpromotedFileName);
                _ = await Pin(RecoveryJournalStore.UnpromotedFileName, RecoveryRules.JournalLimit, false).ConfigureAwait(false);
            }
            var actual = Directory.EnumerateFileSystemEntries(inputs.Operation).Take(names.Count + 1).Select(Path.GetFileName).ToArray();
            if (actual.Length != names.Count || actual.Any(name => !names.Contains(name!, StringComparer.Ordinal)))
                throw new ReleaseValidationException("Unexpected forward operation evidence.");
            var metadata = await Pin("update-manifest.json", ManifestRules.ManifestLimit, true).ConfigureAwait(false);
            var signature = await Pin("update-manifest.sig.json", ManifestRules.SignatureLimit, true).ConfigureAwait(false);
            var bytes = metadata.ReadMetadata(ManifestRules.ManifestLimit);
            var signatureBytes = signature.ReadMetadata(ManifestRules.SignatureLimit);
            var manifest = verifier.Verify(bytes, signatureBytes);
            if (metadata.Snapshot.Sha256 != record.ManifestSha256 || signature.Snapshot.Sha256 != record.SignatureSha256 ||
                manifest.Version != record.TargetVersion || !UpdateEligibility.IsCompatible(manifest, context) ||
                !UpdateEligibility.IsNewerForChannel(manifest.Version, manifest.Channel == "preview", context) ||
                record.TargetApp != new RecoveryFile(true, manifest.Files[0].Size, manifest.Files[0].Sha256))
                throw new ReleaseValidationException("Forward signed identity changed.");
            inputs.Manifest = manifest;
            inputs.Target = await Pin("target-app.exe", ManifestRules.MaximumFileSize, true).ConfigureAwait(false);
            if (inputs.Target.Snapshot != record.TargetApp) throw new ReleaseValidationException("Forward target changed.");
            inspect(Path.Combine(inputs.Operation, "target-app.exe"), manifest.Version, manifest.CommitSha);
            foreach (var (name, expected, maximum) in new[] {
                ("original-app.exe", record.OriginalApp, ManifestRules.MaximumFileSize),
                ("original-settings.json", record.SettingsPrimary, RecoveryRules.SettingsLimit),
                ("original-settings.bak", record.SettingsBackup, RecoveryRules.SettingsLimit) })
                if ((await Pin(name, maximum, name == "original-app.exe").ConfigureAwait(false)).Snapshot != expected)
                    throw new ReleaseValidationException("Forward baseline changed.");
            inspect(Path.Combine(inputs.Operation, "original-app.exe"), context.InstalledVersion, null);
            inputs.AssertCurrentBaseline();
            inputs._checkedAt = time.GetUtcNow();
            inputs._checkTimestamp = time.GetTimestamp();
            var fresh = await discovery.CheckAsync(context, ct).ConfigureAwait(false);
            if (fresh.Status != UpdateCheckStatus.Available || fresh.Candidate is not { } selected ||
                !selected.ManifestBytes.Span.SequenceEqual(bytes) || !selected.SignatureBytes.Span.SequenceEqual(signatureBytes))
                throw new ReleaseValidationException("Forward release must be checked again.");
            inputs.AssertFresh();
            return inputs;

            async Task<PreparationFile> Pin(string name, long limit, bool required)
            {
                var pin = await PreparationFile.OpenAsync(Path.Combine(inputs.Operation, name), limit, required, ct).ConfigureAwait(false);
                inputs._pins.Add(pin);
                return pin;
            }
        }
        catch { inputs.Dispose(); throw; }
    }

    internal void AssertFresh()
    {
        var age = _time.GetUtcNow() - _checkedAt;
        var elapsed = _time.GetElapsedTime(_checkTimestamp);
        if (age < TimeSpan.Zero || elapsed < TimeSpan.Zero || age > TimeSpan.FromHours(24) || elapsed > TimeSpan.FromHours(24))
            throw new ReleaseValidationException("Forward release check expired.");
    }

    internal void AssertCurrentBaseline()
    {
        _lease.AssertHeld();
        _ = _lease.OperationDirectory(Record.OperationId);
        var appDirectory = Path.Combine(_lease.Root, "app");
        ManagedInstallLease.AssertPrivate(appDirectory);
        var entries = Directory.EnumerateFileSystemEntries(appDirectory).Take(2).ToArray();
        if (entries.Length != 1 || Path.GetFileName(entries[0]) != "PrivacyMask.App.exe" ||
            RecoveryRestorer.Inspect(entries[0], ManifestRules.MaximumFileSize) != Record.OriginalApp ||
            RecoveryRestorer.Inspect(Path.Combine(_lease.Root, "settings.v1.json"), RecoveryRules.SettingsLimit) != Record.SettingsPrimary ||
            RecoveryRestorer.Inspect(Path.Combine(_lease.Root, "settings.v1.json.bak"), RecoveryRules.SettingsLimit) != Record.SettingsBackup)
            throw new ReleaseValidationException("Installed baseline changed since preparation.");
    }

    public void Dispose() { foreach (var pin in _pins) pin.Dispose(); _pins.Clear(); }
}
