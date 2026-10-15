using PrivacyMask.Core.Services;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal sealed class AuthenticatedUpdateStartup : IDisposable
{
    private readonly UpdateStartLayout _layout;
    private readonly UpdateReadinessClient _readiness;
    private readonly UpdateSettingsState _migratedSettings;

    private AuthenticatedUpdateStartup(UpdateStartRequest request, UpdateStartLayout layout,
        UpdateReadinessClient readiness, UpdateSettingsMigrationResult migration)
    {
        Request = request;
        _layout = layout;
        _readiness = readiness;
        _migratedSettings = new(ToRecoveryFile(migration.Primary), ToRecoveryFile(migration.Backup));
        ShellContext = new(migration.Settings, layout.LauncherPath,
            request.Mode == UpdateStartMode.Minimized, DeferInteractiveUi: true,
            RequireSuccessfulInitialRefresh: true);
    }

    internal UpdateStartRequest Request { get; }
    internal ShellStartupContext ShellContext { get; }

    internal static bool IsRequested(IReadOnlyList<string> arguments) =>
        UpdateStartRequest.IsRequested(arguments);

    internal static async Task<AuthenticatedUpdateStartup> OpenAsync(string[] arguments,
        CancellationToken cancellationToken)
    {
        var request = UpdateStartRequest.Parse(arguments);
        var layout = UpdateStartLayout.Open(request);
        var baseline = layout.Settings;
        var store = new JsonSettingsStore(new DefaultSettingsFactory(), baseline.PrimaryPath);
        var migration = await store.MigrateForUpdateAsync(new(
            baseline.PrimaryPath,
            baseline.BackupPath,
            baseline.OriginalPrimaryPath,
            baseline.OriginalBackupPath,
            ToSettingsSnapshot(baseline.Primary),
            ToSettingsSnapshot(baseline.Backup)), cancellationToken);
        var migrated = new UpdateSettingsState(ToRecoveryFile(migration.Primary), ToRecoveryFile(migration.Backup));
        layout.AssertMigratedSettings(migrated);
        var readiness = await UpdateReadinessClient.ConnectAsync(request, layout, cancellationToken);
        return new(request, layout, readiness, migration);
    }

    internal async Task SignalReadyAndConfirmCommitAsync(CancellationToken cancellationToken)
    {
        _layout.AssertMigratedSettings(_migratedSettings);
        await _readiness.SignalReadyAsync(cancellationToken);
        await _readiness.ConfirmCommittedAsync(cancellationToken);
    }

    public void Dispose() => _readiness.Dispose();

    private static SettingsFileSnapshot ToSettingsSnapshot(RecoveryFile file) =>
        new(file.Exists, file.Size, file.Sha256);

    private static RecoveryFile ToRecoveryFile(SettingsFileSnapshot file) =>
        new(file.Exists, file.Size, file.Sha256);
}
