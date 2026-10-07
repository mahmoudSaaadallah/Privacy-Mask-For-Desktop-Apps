using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Core.Contracts;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
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
}
