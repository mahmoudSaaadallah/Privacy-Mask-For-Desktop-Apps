using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

// Synthetic binaries test trust/transaction logic only; the real-EXE smoke covers production PE inspection.
internal sealed class PreparationFixture : IDisposable
{
    internal RecoveryFixture Install { get; }
    internal UpdateDiscoveryFixture Trust { get; } = new();
    internal UpdateCheckContext Context => Trust.Context with { WindowsBuild = Environment.OSVersion.Version.Build };
    internal string Stage => Path.Combine(Install.Root, "update-staging", "operation-" + Install.OperationId);
    internal byte[] Manifest => Trust.Handler.Assets[101];
    internal byte[] Signature => Trust.Handler.Assets[102];
    internal byte[] Archive { get; }

    internal PreparationFixture(bool primary = true, bool secondary = true, bool empty = false)
    {
        Install = new(primary, secondary, empty);
        // Remove only the fixture's known pre-existing kernel record/baselines; preparation must create its own.
        File.Delete(Path.Combine(Install.Root, "update-journal.json"));
        foreach (var file in Directory.GetFiles(Install.Operation)) File.Delete(file);
        Directory.Delete(Install.Operation, recursive: false);
        var staging = Path.Combine(Install.Root, "update-staging");
        ManagedInstallLease.CreatePrivateDirectory(staging);
        ManagedInstallLease.CreatePrivateDirectory(Stage);
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            using (var entry = zip.CreateEntry("PrivacyMask.App.exe").Open()) entry.Write(Install.NewApp);
            Archive = memory.ToArray();
        }
        var release = Trust.Release(changeManifest: manifest => manifest with {
            Package = new(ManifestRules.ArchiveName(manifest.Version), Archive.Length, Hash(Archive)),
            Files = [new("PrivacyMask.App.exe", Install.NewApp.Length, Hash(Install.NewApp))] });
        Trust.Handler.Releases.Add(release);
        Write(Path.Combine(Stage, "package.zip"), Archive);
        Write(Path.Combine(Stage, "PrivacyMask.App.exe"), Install.NewApp);
        Write(Path.Combine(Stage, "update-manifest.json"), Manifest);
        Write(Path.Combine(Stage, "update-manifest.sig.json"), Signature);
    }

    internal RecoveryPreparer Preparer(GitHubReleaseClient client, Func<string, long>? free = null,
        Action<PreparationCheckpoint, string>? checkpoint = null, Action<string, string, string?>? inspect = null) =>
        new(Install.Lease, Trust.Verifier, client, Trust.Clock, free, inspect ?? ((_, _, _) => { }), checkpoint);

    internal void AssertUntouched(bool noJournal = true)
    {
        Assert.Equal(Install.OldApp, File.ReadAllBytes(Install.App));
        Assert.Equal(Install.OldPrimary, File.Exists(Install.Primary) ? File.ReadAllBytes(Install.Primary) : null);
        Assert.Equal(Install.OldSecondary, File.Exists(Install.Secondary) ? File.ReadAllBytes(Install.Secondary) : null);
        if (noJournal) Assert.Null(new RecoveryJournalStore(Install.Lease).Read());
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static void Write(string path, byte[] bytes)
    {
        using var output = ManagedInstallLease.CreatePrivateFile(path);
        output.Write(bytes);
        output.Flush(true);
    }
    public void Dispose() { Trust.Dispose(); Install.Dispose(); }
}
