using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

/// <summary>Bounded private scheduling hints, never trust roots, release authority, or user settings.</summary>
internal sealed class UpdateCheckHistoryStore : IUpdateCheckHistoryStore
{
    internal const string FileName = "update-check-history.json";
    internal const int MaximumBytes = 4096;
    private readonly string _root;
    private readonly string _path;
    private readonly string _temporary;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
    };

    internal UpdateCheckHistoryStore(string root)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Path.GetFileName(_root) != ManifestRules.AppId) throw new ArgumentException("Expected a dedicated managed root.");
        _path = Path.Combine(_root, FileName);
        _temporary = Path.Combine(_root, ".update-check-history.tmp");
    }

    public async Task<UpdateCheckHistory> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ManagedInstallLease.AssertPrivate(_root);
        if (!Exists(_path))
        {
            if (Exists(_temporary)) throw new IOException("Interrupted update history requires inspection.");
            return new();
        }
        ManagedInstallLease.AssertPrivate(_path);
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous);
        if (stream.Length is <= 0 or > MaximumBytes) throw new IOException("Invalid update history size.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token);
        using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 4 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in json.RootElement.EnumerateObject())
            if (!names.Add(field.Name)) throw new JsonException("Duplicate update history field.");
        if (!names.SetEquals(["schemaVersion", "lastAttemptUtc", "retryAfterUtc", "stableSuccessUtc", "previewSuccessUtc", "requestPending"]))
            throw new JsonException("Unexpected update history fields.");
        var history = JsonSerializer.Deserialize<UpdateCheckHistory>(bytes, Options) ?? throw new JsonException();
        Validate(history);
        return history;
    }

    public async Task WriteAsync(UpdateCheckHistory history, CancellationToken token)
    {
        Validate(history);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(history, Options);
        if (bytes.Length > MaximumBytes) throw new IOException("Update history exceeded its budget.");
        token.ThrowIfCancellationRequested();
        ManagedInstallLease.AssertPrivate(_root);
        var exists = Exists(_path);
        if (exists) ManagedInstallLease.AssertPrivate(_path);
        // One fixed CreateNew scratch file. Unknown/crashed residue is preserved, not overwritten.
        await using (var stream = ManagedInstallLease.CreatePrivateFile(_temporary, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, token);
            await stream.FlushAsync(token);
            stream.Flush(flushToDisk: true);
        }
        token.ThrowIfCancellationRequested();
        if (exists) File.Replace(_temporary, _path, null, ignoreMetadataErrors: false);
        else File.Move(_temporary, _path, overwrite: false);
    }

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
    }

    private static void Validate(UpdateCheckHistory history)
    {
        if (history.SchemaVersion != 1) throw new JsonException("Unsupported update history schema.");
        if (history.RequestPending && history.LastAttemptUtc is null) throw new JsonException("Invalid pending request history.");
        foreach (var stamp in new[] { history.LastAttemptUtc, history.RetryAfterUtc, history.StableSuccessUtc, history.PreviewSuccessUtc })
            if (stamp is { } value && (value.Offset != TimeSpan.Zero || value < DateTimeOffset.UnixEpoch))
                throw new JsonException("Invalid update history timestamp.");
    }
}
