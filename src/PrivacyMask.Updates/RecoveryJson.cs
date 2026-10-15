using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PrivacyMask.Updates;

// Explicit metadata for the bootstrap's reachable types; no reflection fallback in Native AOT.
internal static class RecoveryJson
{
    internal static readonly RecoveryJsonContext Context = new(new JsonSerializerOptions(ContractJson.Options));
    internal static T Read<T>(byte[] bytes, int limit, JsonTypeInfo<T> type)
    {
        using var document = ContractJson.ReadDocument(bytes, limit);
        return JsonSerializer.Deserialize(bytes, type) ?? throw new ReleaseValidationException("Expected recovery object.");
    }
    internal static byte[] Write<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.SerializeToUtf8Bytes(value, type);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ManagedInstallMarker))]
[JsonSerializable(typeof(RecoveryJournal))]
[JsonSerializable(typeof(ReadinessProbeRequest))]
internal partial class RecoveryJsonContext : JsonSerializerContext;

// DEVELOPER FIXTURE ONLY. These options never authorize an update/apply.
internal enum ReadinessProbeMode { Ready, WrongOperation, WrongChallenge, Disconnect, Stall, WrongProcess, WrongParent, EarlyExit }
internal sealed record ReadinessProbeRequest(int SchemaVersion, string InstallId, string OperationId,
    ReadinessProbeMode Mode, long PeerSize, string PeerSha256, int DeadlineMilliseconds);
