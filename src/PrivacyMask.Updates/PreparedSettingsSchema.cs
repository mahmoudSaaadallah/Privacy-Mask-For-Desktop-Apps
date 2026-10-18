using System.Text.Json;

namespace PrivacyMask.Updates;

internal static class PreparedSettingsSchema
{
    internal static async Task<int> ReadAsync(ManagedInstallLease lease, RecoveryJournal record, CancellationToken token)
    {
        lease.AssertHeld();
        var path = Path.Combine(lease.OperationDirectory(record.OperationId), "original-settings.json");
        using var file = await PreparationFile.OpenAsync(path, RecoveryRules.SettingsLimit, true, token).ConfigureAwait(false);
        if (file.Snapshot != record.SettingsPrimary) throw new ReleaseValidationException("Prepared settings baseline changed.");
        return Parse(file.ReadMetadata((int)RecoveryRules.SettingsLimit));
    }

    internal static int Parse(byte[] bytes)
    {
        if (bytes.Length is 0 or > (int)RecoveryRules.SettingsLimit) throw new ReleaseValidationException("Invalid settings schema input.");
        using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 64 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ReleaseValidationException("Missing settings schema.");
        int? version = null;
        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (!property.Name.Equals("version", StringComparison.OrdinalIgnoreCase)) continue;
            if (version is not null || !property.Value.TryGetInt32(out var value) || value <= 0)
                throw new ReleaseValidationException("Ambiguous settings schema.");
            version = value;
        }
        return version ?? throw new ReleaseValidationException("Missing settings schema.");
    }
}
