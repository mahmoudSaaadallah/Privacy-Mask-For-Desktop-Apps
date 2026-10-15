using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Core.Contracts;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

internal sealed record SettingsFileSnapshot(bool Exists, long Size, string? Sha256);

internal sealed record UpdateSettingsBaseline(
    string PrimaryPath,
    string BackupPath,
    string OriginalPrimaryPath,
    string OriginalBackupPath,
    SettingsFileSnapshot Primary,
    SettingsFileSnapshot Backup);

internal sealed record UpdateSettingsMigrationResult(
    AppSettings Settings,
    SettingsFileSnapshot Primary,
    SettingsFileSnapshot Backup);

internal sealed class SettingsUpdateException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class JsonSettingsStore : ISettingsStore
{
    private const long MaximumUpdateSettingsBytes = 16L * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly DefaultSettingsFactory _defaultSettingsFactory;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    public JsonSettingsStore(DefaultSettingsFactory defaultSettingsFactory, string? settingsPath = null)
    {
        _defaultSettingsFactory = defaultSettingsFactory;
        _settingsPath = settingsPath ?? BuildDefaultSettingsPath();
        _backupPath = $"{_settingsPath}.bak";
    }

    public string SettingsPath => _settingsPath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    internal async Task<UpdateSettingsMigrationResult> MigrateForUpdateAsync(
        UpdateSettingsBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            if (!SamePath(_settingsPath, baseline.PrimaryPath) || !SamePath(_backupPath, baseline.BackupPath))
                throw new SettingsUpdateException("The update settings baseline targets another store.");

            AssertBaseline(baseline);
            AppSettings merged;
            try
            {
                var source = baseline.Primary.Exists
                    ? await ReadExpectedAsync(baseline.OriginalPrimaryPath, baseline.Primary, cancellationToken)
                    : baseline.Backup.Exists
                        ? await ReadExpectedAsync(baseline.OriginalBackupPath, baseline.Backup, cancellationToken)
                        : null;
                merged = source is null ? _defaultSettingsFactory.Create() : DeserializeStrict(source);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException
                or NullReferenceException or ArgumentException)
            {
                throw new SettingsUpdateException("Saved settings cannot be migrated safely.", exception);
            }

            var validation = AppSettingsValidator.Validate(merged);
            if (validation.Count > 0)
                throw new SettingsUpdateException("Saved settings failed validation after migration.");

            var normalized = JsonSerializer.SerializeToUtf8Bytes(merged, SerializerOptions);
            if (baseline.Primary.Exists)
            {
                var current = await ReadExpectedAsync(baseline.PrimaryPath, baseline.Primary, cancellationToken);
                if (current.AsSpan().SequenceEqual(normalized))
                    return Result(merged);
            }

            AssertBaseline(baseline);
            cancellationToken.ThrowIfCancellationRequested();
            var temporaryPath = $"{_settingsPath}.update-{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(normalized, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                AssertBaseline(baseline);
                cancellationToken.ThrowIfCancellationRequested();
                if (baseline.Primary.Exists)
                    File.Replace(temporaryPath, _settingsPath, _backupPath, ignoreMetadataErrors: false);
                else
                    File.Move(temporaryPath, _settingsPath, overwrite: false);
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { /* Preserve the primary result; the managed launcher can inspect residue. */ }
            }

            return Result(merged);
        }
        finally
        {
            _ioGate.Release();
        }

        UpdateSettingsMigrationResult Result(AppSettings settings) => new(settings,
            Inspect(_settingsPath), Inspect(_backupPath));
    }

    private async Task<AppSettings> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return await RestoreBackupOrCreateDefaultsAsync(cancellationToken);
        }

        try
        {
            return await LoadAndNormalizeAsync(_settingsPath, persistNormalization: true, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            PreserveCorruptFile(_settingsPath);
            return await RestoreBackupOrCreateDefaultsAsync(cancellationToken);
        }
    }

    private async Task<AppSettings> RestoreBackupOrCreateDefaultsAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_backupPath))
        {
            try
            {
                var recovered = await LoadAndNormalizeAsync(_backupPath, persistNormalization: false, cancellationToken);
                await SaveCoreAsync(recovered, cancellationToken);
                return recovered;
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                PreserveCorruptFile(_backupPath);
            }
        }

        var defaults = _defaultSettingsFactory.Create();
        await SaveCoreAsync(defaults, cancellationToken);
        return defaults;
    }

    private async Task<AppSettings> LoadAndNormalizeAsync(
        string path,
        bool persistNormalization,
        CancellationToken cancellationToken)
    {
        var persistedText = await File.ReadAllTextAsync(path, cancellationToken);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(persistedText, SerializerOptions);
        var merged = _defaultSettingsFactory.MergeWithDefaults(deserialized);

        if (persistNormalization)
        {
            var normalizedText = JsonSerializer.Serialize(merged, SerializerOptions);
            if (!string.Equals(persistedText, normalizedText, StringComparison.Ordinal))
            {
                await SaveCoreAsync(merged, cancellationToken);
            }
        }

        return merged;
    }

    private async Task SaveCoreAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var normalized = _defaultSettingsFactory.MergeWithDefaults(settings);
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void PreserveCorruptFile(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        var corruptPath = Path.Combine(directory, $"{fileName}.corrupt-{timestamp}{extension}");
        File.Move(path, corruptPath);
    }

    private static string BuildDefaultSettingsPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "PrivacyMask.Desktop", "settings.v1.json");
    }

    private AppSettings DeserializeStrict(byte[] source)
    {
        using var document = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.EnumerateObject().Any(property =>
                string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out _)))
            throw new SettingsUpdateException("Saved settings do not declare a supported schema.");
        var persisted = JsonSerializer.Deserialize<AppSettings>(source, SerializerOptions)
            ?? throw new SettingsUpdateException("Saved settings are empty.");
        if (persisted.Version is <= 0 or > AppSettings.CurrentVersion)
            throw new SettingsUpdateException("Saved settings use an unsupported schema.");
        if (persisted.PanicPin is not null && !PanicPinService.IsCredentialValid(persisted.PanicPin))
            throw new SettingsUpdateException("Saved PIN protection is invalid.");
        return _defaultSettingsFactory.MergeWithDefaults(persisted);
    }

    private static async Task<byte[]> ReadExpectedAsync(string path, SettingsFileSnapshot expected,
        CancellationToken cancellationToken)
    {
        if (!expected.Exists || expected.Size > MaximumUpdateSettingsBytes)
            throw new SettingsUpdateException("Invalid update settings snapshot.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length != expected.Size) throw new SettingsUpdateException("Saved settings changed during update.");
        var bytes = new byte[checked((int)file.Length)];
        await file.ReadExactlyAsync(bytes, cancellationToken);
        if (file.ReadByte() != -1 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != expected.Sha256)
            throw new SettingsUpdateException("Saved settings changed during update.");
        return bytes;
    }

    private static void AssertBaseline(UpdateSettingsBaseline baseline)
    {
        if (Inspect(baseline.PrimaryPath) != baseline.Primary ||
            Inspect(baseline.BackupPath) != baseline.Backup ||
            Inspect(baseline.OriginalPrimaryPath) != baseline.Primary ||
            Inspect(baseline.OriginalBackupPath) != baseline.Backup)
            throw new SettingsUpdateException("Saved settings changed during update.");
    }

    private static SettingsFileSnapshot Inspect(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return new(false, 0, null); }
        if ((attributes & FileAttributes.Directory) != 0)
            throw new SettingsUpdateException("A settings file path is a directory.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumUpdateSettingsBytes)
            throw new SettingsUpdateException("Saved settings exceed the update limit.");
        var size = file.Length;
        var hash = Convert.ToHexStringLower(SHA256.HashData(file));
        if (file.Length != size) throw new SettingsUpdateException("Saved settings changed during update.");
        return new(true, size, hash);
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
