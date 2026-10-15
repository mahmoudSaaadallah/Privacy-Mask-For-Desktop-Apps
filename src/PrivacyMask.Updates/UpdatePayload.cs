using System.Diagnostics;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace PrivacyMask.Updates;

/// <summary>Shared publisher/downloader checks. Never executes the payload.</summary>
public static class UpdatePayload
{
    /// <summary>Bound central-directory allocation before ZipArchive parses untrusted entries.</summary>
    public static void ValidateArchiveLayout(Stream archive)
    {
        if (!archive.CanSeek || archive.Length is < 22 or > ManifestRules.MaximumFileSize)
            throw new ReleaseValidationException("Unsupported archive bounds.");
        Span<byte> end = stackalloc byte[22];
        archive.Position = archive.Length - end.Length;
        archive.ReadExactly(end);
        if (BinaryPrimitives.ReadUInt32LittleEndian(end) != 0x06054b50 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[4..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[6..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[8..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[10..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[20..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(end[12..]) is < 46 or > 4096 ||
            (long)BinaryPrimitives.ReadUInt32LittleEndian(end[16..]) + BinaryPrimitives.ReadUInt32LittleEndian(end[12..]) != archive.Length - 22)
            throw new ReleaseValidationException("Only a bounded single-entry ZIP without comments or ZIP64 is supported.");
        archive.Position = 0;
    }

    public static void InspectExecutable(string path, string version, string commit, string? expectedFileVersion = null)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32Plus ||
            (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0)
            throw new ReleaseValidationException("Payload must be a Windows x64 executable.");
        var identity = FileVersionInfo.GetVersionInfo(path);
        var fileVersion = identity.FileVersion ?? "";
        var parts = fileVersion.Split('.');
        var core = version.Split('-')[0];
        if (identity.ProductName != "PrivacyMask for Desktop Apps" || identity.ProductVersion != version + "+" + commit ||
            parts.Length != 4 || string.Join('.', parts.Take(3)) != core ||
            parts.Any(part => !int.TryParse(part, out var value) || value is < 0 or > 65534) ||
            (expectedFileVersion is not null && fileVersion != expectedFileVersion))
            throw new ReleaseValidationException("Executable product, numeric version or source identity is inconsistent.");
    }

    public static void ValidateEntry(ZipArchive zip, PayloadFile expected)
    {
        if (zip.Entries.Count != 1) throw new ReleaseValidationException("Update archive must contain exactly one file.");
        var entry = zip.Entries[0];
        var unixType = ((uint)entry.ExternalAttributes >> 16) & 0xf000;
        if (entry.FullName != "PrivacyMask.App.exe" || expected.Path != entry.FullName || entry.Length != expected.Size ||
            expected.Size is <= 0 or > ManifestRules.MaximumFileSize ||
            unixType is not (0 or 0x8000) || (entry.ExternalAttributes & ((int)FileAttributes.ReparsePoint | (int)FileAttributes.Directory)) != 0)
            throw new ReleaseValidationException("Archive entry violates the regular-file allowlist.");
    }

    public static async Task CopyVerifiedAsync(Stream input, Stream output, long expectedSize, string expectedHash,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (expectedSize is <= 0 or > ManifestRules.MaximumFileSize || !ManifestRules.IsHash(expectedHash))
            throw new ReleaseValidationException("Invalid stream bounds/hash.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total = checked(total + read);
            if (total > expectedSize) throw new ReleaseValidationException("Stream exceeded signed size.");
            hash.AppendData(buffer.AsSpan(0, read));
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(total);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (total != expectedSize || Convert.ToHexStringLower(hash.GetHashAndReset()) != expectedHash)
            throw new ReleaseValidationException("Stream hash/size mismatch.");
    }
}
