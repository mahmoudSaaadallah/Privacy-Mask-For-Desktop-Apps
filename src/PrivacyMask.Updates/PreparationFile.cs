using System.Security.Cryptography;

namespace PrivacyMask.Updates;

/// <summary>Bounded, pinned input. Read sharing excludes cooperating writers and renames until disposed.</summary>
internal sealed class PreparationFile : IDisposable
{
    private readonly FileStream? _stream;
    internal RecoveryFile Snapshot { get; }

    private PreparationFile(FileStream? stream, RecoveryFile snapshot) { _stream = stream; Snapshot = snapshot; }

    internal static async Task<PreparationFile> OpenAsync(string path, long limit, bool required, CancellationToken ct)
    {
        StagingWorkspace.AssertSafe(path);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) when (!required) { return new(null, new(false, 0, null)); }
        if ((attributes & FileAttributes.Directory) != 0) throw new ReleaseValidationException("Preparation input is not a regular file.");
        ManagedInstallLease.AssertPrivate(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var size = stream.Length;
            if (size > limit || (required && size == 0)) throw new ReleaseValidationException("Preparation input size is invalid.");
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
            if (stream.Length != size) throw new ReleaseValidationException("Preparation input changed.");
            stream.Position = 0;
            return new(stream, new(true, size, hash));
        }
        catch { stream.Dispose(); throw; }
    }

    internal byte[] ReadMetadata(int limit)
    {
        if (_stream is null || Snapshot.Size <= 0 || Snapshot.Size > limit)
            throw new ReleaseValidationException("Preparation metadata bounds are invalid.");
        _stream.Position = 0;
        var bytes = new byte[(int)Snapshot.Size];
        _stream.ReadExactly(bytes);
        if (_stream.ReadByte() != -1) throw new ReleaseValidationException("Preparation metadata changed.");
        return bytes;
    }

    internal async Task CopyToAsync(string target, CancellationToken ct)
    {
        if (_stream is null) return; // Exact original absence: do not manufacture an empty backup.
        _stream.Position = 0;
        using (var output = ManagedInstallLease.CreatePrivateFile(target, 64 * 1024))
        {
            if (Snapshot.Size == 0)
            {
                ct.ThrowIfCancellationRequested();
                if (_stream.ReadByte() != -1) throw new ReleaseValidationException("Empty settings changed.");
            }
            else await UpdatePayload.CopyVerifiedAsync(_stream, output, Snapshot.Size, Snapshot.Sha256!, cancellationToken: ct).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        ManagedInstallLease.AssertPrivate(target);
    }

    public void Dispose() => _stream?.Dispose();
}
