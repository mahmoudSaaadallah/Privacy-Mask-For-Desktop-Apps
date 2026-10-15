using System.Security.AccessControl;
using System.Security.Principal;

namespace PrivacyMask.Updates;

/// <summary>Private layout/lock prototype, not a legacy-install detector or migration API.</summary>
internal sealed class ManagedInstallLease : IDisposable
{
    private readonly FileStream _lock;
    private bool _disposed;
    internal string Root { get; }
    internal string InstallId { get; }

    internal ManagedInstallLease(string root, string installId)
    {
        if (!OperatingSystem.IsWindows() || !RecoveryRules.IsId(installId) || !Path.IsPathFullyQualified(root))
            throw new ReleaseValidationException("Managed installation identity is invalid.");
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Root.StartsWith(@"\\", StringComparison.Ordinal) || Root.Length < 4 || Root[1] != ':' || Root[2] != '\\' ||
            Root[3..].Contains(':') || Path.GetFileName(Root) != ManifestRules.AppId ||
            !Directory.Exists(Root) || new DriveInfo(Path.GetPathRoot(Root)!).DriveType != DriveType.Fixed)
            throw new ReleaseValidationException("Unsupported managed installation location.");
        InstallId = installId;
        AssertPrivate(Root);
        ValidateMarker();
        var lockPath = Path.Combine(Root, ".update.lock");
        StagingWorkspace.AssertSafe(lockPath);
        FileStream handle;
        try { handle = CreatePrivateFile(lockPath, 1); }
        catch (IOException) when (File.Exists(lockPath))
        {
            // Existing files must pass validation; never silently take ownership or weaken their ACL.
            AssertPrivate(lockPath);
            handle = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
        }
        try
        {
            AssertPrivate(lockPath);
            ValidateMarker(); // Repeat under the exclusive lease.
            _lock = handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal void AssertHeld()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AssertPrivate(Root);
        AssertPrivate(Path.Combine(Root, ".update.lock"));
        ValidateMarker();
    }

    internal string OperationDirectory(string operationId)
    {
        AssertHeld();
        if (!RecoveryRules.IsId(operationId)) throw new ReleaseValidationException("Invalid operation identifier.");
        var operations = Path.Combine(Root, "update-operations");
        var operation = Path.Combine(operations, "operation-" + operationId);
        AssertPrivate(operations);
        AssertPrivate(operation);
        return operation;
    }

    private void ValidateMarker()
    {
        var path = Path.Combine(Root, "managed-install.json");
        AssertPrivate(path);
        var marker = RecoveryJson.Read(ReadBounded(path, 4096), 4096, RecoveryJson.Context.ManagedInstallMarker);
        RecoveryRules.Validate(marker);
        if (marker.InstallId != InstallId) throw new ReleaseValidationException("Managed install marker changed.");
    }

    internal static byte[] ReadBounded(string path, int limit)
    {
        StagingWorkspace.AssertSafe(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 || file.Length > limit) throw new ReleaseValidationException("Recovery metadata size is invalid.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new ReleaseValidationException("Recovery metadata changed during reading.");
        return bytes;
    }

    internal static FileStream CreatePrivateFile(string path, int bufferSize = 4096, FileOptions options = FileOptions.WriteThrough)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        StagingWorkspace.AssertSafe(path);
        AssertPrivate(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new ReleaseValidationException("Current user SID unavailable.");
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None,
            bufferSize, options, security);
    }

    internal static void CreatePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        StagingWorkspace.AssertSafe(path);
        AssertPrivate(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Only provision new operation directories, never take over existing evidence.
        if (Path.Exists(path)) throw new ReleaseValidationException("Managed directory already exists.");
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new ReleaseValidationException("Current user SID unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security);
        AssertPrivate(path);
    }

    internal static void AssertPrivate(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        StagingWorkspace.AssertSafe(path);
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new ReleaseValidationException("Current user SID unavailable.");
        FileSystemSecurity security;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0) security = new DirectoryInfo(path).GetAccessControl();
        else security = new FileInfo(path).GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        var ownsFullControl = false;
        foreach (var rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (!user.Equals(rule.IdentityReference)) throw new ReleaseValidationException("Managed path grants another identity access.");
            ownsFullControl |= (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;
        }
        if (!user.Equals(security.GetOwner(typeof(SecurityIdentifier))) || !ownsFullControl)
            throw new ReleaseValidationException("Managed paths must be owned and accessible only by the current user.");
    }

    public void Dispose() { if (!_disposed) { _disposed = true; _lock.Dispose(); } }
}
