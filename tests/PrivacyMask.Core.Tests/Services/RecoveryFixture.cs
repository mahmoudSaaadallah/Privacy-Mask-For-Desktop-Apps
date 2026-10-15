using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

internal sealed class RecoveryFixture : IDisposable
{
    internal string Parent { get; } = Path.Combine(Path.GetTempPath(), "PrivacyMask-recovery-test-" + Guid.NewGuid().ToString("N"));
    internal string Root => Path.Combine(Parent, ManifestRules.AppId);
    internal string InstallId { get; } = Guid.NewGuid().ToString("N");
    internal string OperationId { get; } = Guid.NewGuid().ToString("N");
    internal string Operation => Path.Combine(Root, "update-operations", "operation-" + OperationId);
    internal string App => Path.Combine(Root, "app", "PrivacyMask.App.exe");
    internal string Primary => Path.Combine(Root, "settings.v1.json");
    internal string Secondary => Primary + ".bak";
    internal byte[] OldApp { get; } = Encoding.UTF8.GetBytes("synthetic original app, never executed");
    internal byte[] NewApp { get; } = Encoding.UTF8.GetBytes("synthetic replacement app, never executed");
    internal byte[]? OldPrimary { get; }
    internal byte[]? OldSecondary { get; }
    internal ManagedInstallLease Lease { get; private set; }
    internal RecoveryJournal Record { get; private set; }

    internal RecoveryFixture(bool primary = true, bool secondary = true, bool emptySettings = false)
    {
        Directory.CreateDirectory(Parent);
        using var identity = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetOwner(identity.User!);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Root).Create(security);
        new DirectoryInfo(Path.GetDirectoryName(App)!).Create(security);
        new DirectoryInfo(Path.GetDirectoryName(Operation)!).Create(security);
        new DirectoryInfo(Operation).Create(security);
        WriteFile(Path.Combine(Root, "managed-install.json"), ContractJson.Write(new ManagedInstallMarker(1, ManifestRules.AppId, InstallId, 1)));
        WriteFile(App, OldApp);
        OldPrimary = primary ? (emptySettings ? [] : Encoding.UTF8.GetBytes("{\"testVerifier\":\"never-put-this-marker-in-the-journal\"}")) : null;
        OldSecondary = secondary ? (emptySettings ? [] : Encoding.UTF8.GetBytes("older saved backup")) : null;
        if (OldPrimary is not null) WriteFile(Primary, OldPrimary);
        if (OldSecondary is not null) WriteFile(Secondary, OldSecondary);
        WriteFile(Path.Combine(Operation, "original-app.exe"), OldApp);
        if (OldPrimary is not null) WriteFile(Path.Combine(Operation, "original-settings.json"), OldPrimary);
        if (OldSecondary is not null) WriteFile(Path.Combine(Operation, "original-settings.bak"), OldSecondary);
        Record = new(1, InstallId, OperationId, 1, RecoveryStep.Prepared, "2.0.0-beta.2", "2.0.0-beta.3", Snapshot(OldApp), Snapshot(NewApp),
            Snapshot(OldPrimary), Snapshot(OldSecondary), new string('a', 64), new string('b', 64));
        Lease = new(Root, InstallId);
        new RecoveryJournalStore(Lease).Write(Record);
    }

    internal void Advance(RecoveryStep target)
    {
        foreach (var step in new[] { RecoveryStep.Handoff, RecoveryStep.Replacing, RecoveryStep.AwaitingReadiness, RecoveryStep.Committed })
        {
            if ((int)step <= (int)Record.Step) continue;
            if ((int)step > (int)target) break;
            Record = Record with { Step = step, Revision = Record.Revision + 1 };
            new RecoveryJournalStore(Lease).Write(Record);
        }
    }
    internal void ReplaceFiles()
    {
        WriteFile(App, NewApp);
        WriteFile(Primary, Encoding.UTF8.GetBytes("new schema primary"));
        WriteFile(Secondary, Encoding.UTF8.GetBytes("new schema backup"));
    }
    private static void WriteFile(string path, byte[] bytes)
    {
        if (File.Exists(path)) { File.WriteAllBytes(path, bytes); return; }
        using var file = ManagedInstallLease.CreatePrivateFile(path);
        file.Write(bytes);
    }
    internal void Reopen() { Lease.Dispose(); Lease = new(Root, InstallId); }
    internal void AssertOriginal()
    {
        Assert.Equal(OldApp, File.ReadAllBytes(App));
        AssertFile(Primary, OldPrimary);
        AssertFile(Secondary, OldSecondary);
        Assert.Equal(OldApp, File.ReadAllBytes(Path.Combine(Operation, "original-app.exe")));
    }
    private static void AssertFile(string path, byte[]? expected)
    {
        if (expected is null) Assert.False(File.Exists(path));
        else Assert.Equal(expected, File.ReadAllBytes(path));
    }
    internal static RecoveryFile Snapshot(byte[]? bytes) => bytes is null ? new(false, 0, null)
        : new(true, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    public void Dispose()
    {
        Lease.Dispose();
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!Parent.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Parent).StartsWith("PrivacyMask-recovery-test-"))
            throw new InvalidOperationException("Unsafe fixture cleanup.");
        StagingWorkspace.AssertSafe(Parent);
        Directory.Delete(Parent, recursive: true);
    }
}

internal sealed class SimulatedRecoveryCrash : Exception;
