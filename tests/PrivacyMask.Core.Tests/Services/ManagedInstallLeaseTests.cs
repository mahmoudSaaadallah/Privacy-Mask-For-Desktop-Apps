using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ManagedInstallLeaseTests
{
    [Fact]
    public void NewlyCreatedRecoveryFilesHaveExplicitUserOwnershipAndCannotOverwrite()
    {
        using var f = new RecoveryFixture();
        var path = Path.Combine(f.Operation, "private-fixture.tmp");
        using (var file = ManagedInstallLease.CreatePrivateFile(path)) file.WriteByte(42);
        using var identity = WindowsIdentity.GetCurrent();
        var security = new FileInfo(path).GetAccessControl();
        Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        ManagedInstallLease.AssertPrivate(path);
        Assert.Throws<IOException>(() => ManagedInstallLease.CreatePrivateFile(path));
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void LeaseExcludesAnotherWindowsProcessAndCanBeReacquiredAfterExit()
    {
        using var f = new RecoveryFixture();
        Assert.Equal(73, ProbeFileLease(f.Root));
        f.Lease.Dispose();
        Assert.Equal(0, ProbeFileLease(f.Root));
        f.Reopen();
        f.Lease.AssertHeld();
    }

    [Fact]
    public void Open_reads_the_private_marker_identity_before_acquiring_the_lease()
    {
        using var fixture = new RecoveryFixture();
        fixture.Lease.Dispose();

        using var reopened = ManagedInstallLease.Open(fixture.Root);

        Assert.Equal(fixture.InstallId, reopened.InstallId);
        reopened.AssertHeld();
    }

    private static int ProbeFileLease(string root)
    {
        var powershell = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("try { $probe = [IO.File]::Open($env:PRIVACYMASK_TEST_LOCK_PATH, 'Open', 'ReadWrite', 'None'); $probe.Dispose(); exit 0 } catch { exit 73 }");
        start.Environment["PRIVACYMASK_TEST_LOCK_PATH"] = Path.Combine(root, ".update.lock");
        using var child = Process.Start(start)!;
        if (!child.WaitForExit(15000)) { child.Kill(); throw new TimeoutException("Owned fixture lock probe timed out."); }
        return child.ExitCode;
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("broad")]
    [InlineData("missing-marker")]
    [InlineData("wrong-id")]
    [InlineData("public-acl")]
    public void InvalidRootsNeverAcquireAnInstallLease(string invalid)
    {
        using var f = new RecoveryFixture();
        var root = f.Root;
        var id = f.InstallId;
        if (invalid == "relative") root = ManifestRules.AppId;
        if (invalid == "broad") root = Path.GetPathRoot(f.Root)!;
        if (invalid == "missing-marker") File.Delete(Path.Combine(f.Root, "managed-install.json"));
        if (invalid == "wrong-id") id = Guid.NewGuid().ToString("N");
        if (invalid == "public-acl")
        {
            var directory = new DirectoryInfo(f.Root);
            var security = directory.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        if (invalid == "missing-marker") Assert.Throws<FileNotFoundException>(() => new ManagedInstallLease(root, id));
        else Assert.Throws<ReleaseValidationException>(() => new ManagedInstallLease(root, id));
        Assert.Equal(f.OldApp, File.ReadAllBytes(f.App));
    }

    [Fact]
    public void OperationJunctionCannotRedirectRecoveryIntoAnotherDirectory()
    {
        using var f = new RecoveryFixture();
        var original = f.Operation + "-saved";
        var outside = Path.Combine(f.Parent, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "untouched");
        Directory.Move(f.Operation, original);
        var start = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c mklink /J \"{f.Operation}\" \"{outside}\"", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
            Assert.Equal("untouched", File.ReadAllText(sentinel));
            Assert.Equal(f.OldApp, File.ReadAllBytes(f.App));
        }
        finally { Directory.Delete(f.Operation); Directory.Move(original, f.Operation); }
    }

    [Fact]
    public void OversizedSettingsAbortBeforeAppRestoration()
    {
        using var f = new RecoveryFixture();
        f.ReplaceFiles();
        using (var file = File.OpenWrite(f.Primary)) file.SetLength(RecoveryRules.SettingsLimit + 1);
        Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
        Assert.Equal(f.NewApp, File.ReadAllBytes(f.App));
        Assert.Equal(RecoveryStep.Prepared, new RecoveryJournalStore(f.Lease).Read()!.Step);
    }
}
