using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PrivacyMask.Updates;

public sealed record PayloadFile(string Path, long Size, string Sha256);
public sealed record UpdateArchive(string AssetName, long Size, string Sha256);
public sealed record SettingsCompatibility(int MinimumReadableSchema, int MaximumReadableSchema, int OutputSchema);
public sealed record UpdateManifest(int SchemaVersion, string AppId, string Repository, string Version,
    string Tag, string CommitSha, string Channel, string Runtime, int MinWindowsBuild,
    int MinUpdaterProtocol, UpdateArchive Package, PayloadFile[] Files, SettingsCompatibility SettingsCompatibility);
public sealed record SignatureEnvelope(string Algorithm, string KeyId, string Signature);
public sealed record TrustedReleaseKey(string KeyId, string PublicKeySpkiBase64);
public sealed record ReleasePolicy(int SchemaVersion, int? MinimumWindowsBuild, int MinimumUpdaterProtocol,
    SettingsCompatibility SettingsCompatibility, TrustedReleaseKey[] TrustedKeys);

public sealed class ReleaseValidationException(string message) : Exception(message);

public static class ContractJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };

    public static byte[] Write<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T Read<T>(byte[] bytes, int maximumBytes)
    {
        if (bytes.Length == 0 || bytes.Length > maximumBytes ||
            bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            throw new ReleaseValidationException("Invalid JSON byte length or encoding.");
        // Strict decoding rejects invalid UTF-8 before JSON parsing or normalization.
        _ = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        CheckDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new ReleaseValidationException("JSON must be an object.");
    }

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ReleaseValidationException("Duplicate JSON property.");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicates(child);
    }
}

public static class ManifestRules
{
    public const string Repository = "mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps";
    public const string AppId = "PrivacyMask.Desktop";
    public const long MaximumFileSize = 512L * 1024 * 1024;
    public const int ManifestLimit = 64 * 1024;
    public const int SignatureLimit = 4 * 1024;
    public const string Algorithm = "ECDSA-P256-SHA256";
    private const string ProductPattern = @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?\z";

    public static bool IsVersion(string version) => version.Length <= 128 && Matches(version, ProductPattern);
    public static string ArchiveName(string version) => $"PrivacyMask-{version}-win-x64-update.zip";
    public static bool IsKeyId(string keyId) => keyId.Length is >= 1 and <= 64 && Matches(keyId, @"\A[a-z0-9][a-z0-9-]*\z");
    public static bool IsHash(string hash) => Matches(hash, @"\A[0-9a-f]{64}\z");
    public static bool IsCommit(string commit) => Matches(commit, @"\A[0-9a-f]{40}\z");
    private static bool Matches(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static void Validate(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.AppId != AppId || manifest.Repository != Repository ||
            !IsVersion(manifest.Version) || manifest.Tag != "v" + manifest.Version || !IsCommit(manifest.CommitSha) ||
            manifest.Channel != (manifest.Version.Contains('-') ? "preview" : "stable") || manifest.Runtime != "win-x64" ||
            manifest.MinWindowsBuild <= 0 || manifest.MinUpdaterProtocol <= 0)
            throw new ReleaseValidationException("Unsupported or inconsistent update identity/compatibility.");
        if (manifest.Package.AssetName != ArchiveName(manifest.Version) || !IsHash(manifest.Package.Sha256) ||
            manifest.Package.Size is <= 0 or > MaximumFileSize || manifest.Files.Length != 1)
            throw new ReleaseValidationException("Invalid update archive contract.");
        var file = manifest.Files[0];
        if (file.Path != "PrivacyMask.App.exe" || !IsHash(file.Sha256) || file.Size is <= 0 or > MaximumFileSize ||
            file.Size > checked(manifest.Package.Size * 100))
            throw new ReleaseValidationException("Invalid app-only payload contract or expansion ratio.");
        Validate(manifest.SettingsCompatibility);
    }

    public static void Validate(SettingsCompatibility settings)
    {
        if (settings.MinimumReadableSchema <= 0 || settings.MaximumReadableSchema < settings.MinimumReadableSchema ||
            settings.OutputSchema < settings.MinimumReadableSchema || settings.OutputSchema > settings.MaximumReadableSchema)
            throw new ReleaseValidationException("Invalid settings compatibility interval.");
    }

    public static void Validate(ReleasePolicy policy)
    {
        if (policy.SchemaVersion != 1 || policy.MinimumWindowsBuild is null or <= 0 || policy.MinimumUpdaterProtocol != 1 ||
            policy.TrustedKeys.Length == 0)
            throw new ReleaseValidationException("Release policy is unconfigured. Approve OS qualification and a pinned public key first.");
        Validate(policy.SettingsCompatibility);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in policy.TrustedKeys)
        {
            if (!IsKeyId(key.KeyId) || !ids.Add(key.KeyId)) throw new ReleaseValidationException("Invalid or duplicate trusted key ID.");
            using var publicKey = UpdateManifestVerifier.ImportPublicKey(key.PublicKeySpkiBase64);
        }
    }
}
