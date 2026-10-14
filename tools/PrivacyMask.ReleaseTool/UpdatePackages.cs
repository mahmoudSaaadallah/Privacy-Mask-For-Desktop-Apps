using System.IO.Compression;
using System.Security.Cryptography;
using PrivacyMask.Updates;

namespace PrivacyMask.ReleaseTool;

public static class UpdatePackages
{
    public static string Hash(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    public static byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
            throw new ReleaseValidationException("Metadata file exceeds its allowed size.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new ReleaseValidationException("Metadata changed during reading.");
        return bytes;
    }

    public static void InspectExecutable(string path, string version, string commit, string? expectedFileVersion = null) =>
        UpdatePayload.InspectExecutable(path, version, commit, expectedFileVersion);

    public static UpdateManifest Create(string executable, string outputDirectory, string version, string fileVersion,
        string commit, ReleasePolicy policy, string keyId, string privateKeyPem)
    {
        ManifestRules.Validate(policy);
        if (!ManifestRules.IsVersion(version) || !ManifestRules.IsCommit(commit))
            throw new ReleaseValidationException("Invalid requested release identity.");
        var output = Path.GetFullPath(outputDirectory);
        AssertNoReparsePoints(output);
        AssertNoReparsePoints(Path.GetFullPath(executable));
        var archivePath = Path.Combine(output, ManifestRules.ArchiveName(version));
        var manifestPath = Path.Combine(output, "update-manifest.json");
        var signaturePath = Path.Combine(output, "update-manifest.sig.json");
        if (new[] { archivePath, manifestPath, signaturePath }.Any(File.Exists))
            throw new ReleaseValidationException("Refusing to overwrite existing update assets. Use a fresh output directory.");

        using var source = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length is <= 0 or > ManifestRules.MaximumFileSize)
            throw new ReleaseValidationException("Payload size is not supported.");
        InspectExecutable(executable, version, commit, fileVersion);
        var payload = new PayloadFile("PrivacyMask.App.exe", source.Length, Hash(source));
        source.Position = 0;
        // Validate private key BEFORE writing any output, using a structurally valid stub.
        var stub = new UpdateManifest(1, ManifestRules.AppId, ManifestRules.Repository, version, "v" + version, commit,
            version.Contains('-') ? "preview" : "stable", "win-x64", policy.MinimumWindowsBuild!.Value,
            policy.MinimumUpdaterProtocol, new(ManifestRules.ArchiveName(version), payload.Size, new string('0', 64)),
            [payload], policy.SettingsCompatibility);
        _ = ManifestSigning.Sign(ContractJson.Write(stub), privateKeyPem, keyId, policy);
        Directory.CreateDirectory(output);
        var ownedFiles = new List<string>();
        try
        {
            using (var target = NewOwnedFile(archivePath, ownedFiles))
            using (var zip = new ZipArchive(target, ZipArchiveMode.Create))
            using (var entry = zip.CreateEntry("PrivacyMask.App.exe", CompressionLevel.Optimal).Open())
                source.CopyTo(entry, 64 * 1024);

            using var archive = File.OpenRead(archivePath);
            var manifest = stub with { Package = new(Path.GetFileName(archivePath), archive.Length, Hash(archive)) };
            ManifestRules.Validate(manifest);
            var manifestBytes = ContractJson.Write(manifest);
            var signature = ManifestSigning.Sign(manifestBytes, privateKeyPem, keyId, policy);
            WriteOwnedFile(manifestPath, manifestBytes, ownedFiles);
            WriteOwnedFile(signaturePath, ContractJson.Write(signature), ownedFiles);
            Verify(output, policy, version, commit, fileVersion);
            return manifest;
        }
        catch
        {
            // Only paths whose CreateNew call succeeded belong to this operation.
            foreach (var path in ownedFiles) File.Delete(path);
            throw;
        }
    }

    public static UpdateManifest Verify(string directory, ReleasePolicy policy, string version, string commit,
        string? expectedFileVersion = null)
    {
        var root = Path.GetFullPath(directory);
        AssertNoReparsePoints(root);
        var manifest = ManifestSigning.Verify(ReadBounded(Path.Combine(root, "update-manifest.json"), ManifestRules.ManifestLimit),
            ReadBounded(Path.Combine(root, "update-manifest.sig.json"), ManifestRules.SignatureLimit), policy);
        if (manifest.Version != version || manifest.CommitSha != commit)
            throw new ReleaseValidationException("Update assets do not describe the requested source/version.");
        var archivePath = Path.Combine(root, manifest.Package.AssetName);
        AssertNoReparsePoints(archivePath);
        using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (archive.Length != manifest.Package.Size || Hash(archive) != manifest.Package.Sha256)
            throw new ReleaseValidationException("Archive hash/size does not match signed metadata.");
        archive.Position = 0;
        UpdatePayload.ValidateArchiveLayout(archive);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        ValidateEntry(zip, manifest.Files[0]);
        var scratch = Path.Combine(root, ".verify-" + Guid.NewGuid().ToString("N") + ".exe");
        var ownedFiles = new List<string>();
        try
        {
            using (var target = NewOwnedFile(scratch, ownedFiles))
            using (var entry = zip.Entries[0].Open()) CopyBounded(entry, target, manifest.Files[0]);
            InspectExecutable(scratch, manifest.Version, manifest.CommitSha, expectedFileVersion);
        }
        finally { foreach (var path in ownedFiles) File.Delete(path); }
        return manifest;
    }

    public static void ValidateEntry(ZipArchive zip, PayloadFile expected) => UpdatePayload.ValidateEntry(zip, expected);

    public static void CopyBounded(Stream input, Stream output, PayloadFile expected) =>
        UpdatePayload.CopyVerifiedAsync(input, output, expected.Size, expected.Sha256).GetAwaiter().GetResult();

    private static FileStream NewOwnedFile(string path, List<string> owned)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        owned.Add(path);
        return stream;
    }

    private static void WriteOwnedFile(string path, byte[] bytes, List<string> owned)
    {
        using var stream = NewOwnedFile(path, owned);
        stream.Write(bytes);
        stream.Flush(true);
    }

    private static void AssertNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ReleaseValidationException("Release tooling does not accept reparse-point paths.");
    }
}
