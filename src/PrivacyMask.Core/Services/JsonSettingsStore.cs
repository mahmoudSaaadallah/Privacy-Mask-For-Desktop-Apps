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

    public JsonSettingsStore(DefaultSettingsFactory defaultSettingsFactory, string? settingsPath = null)
    {
        _defaultSettingsFactory = defaultSettingsFactory;
        _settingsPath = settingsPath ?? BuildDefaultSettingsPath();
    }

    public string SettingsPath => _settingsPath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = _defaultSettingsFactory.Create();
            await SaveAsync(defaults, cancellationToken);
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
            await SaveAsync(merged, cancellationToken);
            return merged;
        }
        catch
        {
            var defaults = _defaultSettingsFactory.Create();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = _defaultSettingsFactory.MergeWithDefaults(settings);
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
    }

    private static string BuildDefaultSettingsPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "PrivacyMask.Desktop", "settings.v1.json");
    }
}
