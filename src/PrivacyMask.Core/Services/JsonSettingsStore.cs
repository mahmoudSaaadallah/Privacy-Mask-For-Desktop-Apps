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
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    public JsonSettingsStore(DefaultSettingsFactory defaultSettingsFactory, string? settingsPath = null)
    {
        _defaultSettingsFactory = defaultSettingsFactory;
        _settingsPath = settingsPath ?? BuildDefaultSettingsPath();
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
            var defaults = _defaultSettingsFactory.Create();
            await SaveCoreAsync(defaults, cancellationToken);
            return defaults;
        }

        try
        {
            AppSettings? deserialized;
            await using (var stream = File.OpenRead(_settingsPath))
            {
                deserialized = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken);
            }

            var merged = _defaultSettingsFactory.MergeWithDefaults(deserialized);
            await SaveCoreAsync(merged, cancellationToken);
            return merged;
        }
        catch (JsonException)
        {
            var defaults = _defaultSettingsFactory.Create();
            await SaveCoreAsync(defaults, cancellationToken);
            return defaults;
        }
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

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string BuildDefaultSettingsPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "PrivacyMask.Desktop", "settings.v1.json");
    }
}
