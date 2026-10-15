using System.Security.AccessControl;
using System.Security.Principal;

namespace PrivacyMask.Updates;

/// <summary>Owns only new operation files. No recursive cleanup or installation writes.</summary>
internal sealed class StagingWorkspace
{
    internal const long SafetyMargin = 64L * 1024 * 1024;
    private readonly List<string> _owned = [];
    private bool _cleaned;
    public string Root { get; }

    public StagingWorkspace(string stagingRoot, UpdateManifest manifest, Func<string, long>? availableSpace = null)
    {
        if (!Path.IsPathFullyQualified(stagingRoot)) throw new ReleaseValidationException("An absolute staging root is required.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
        if (!OperatingSystem.IsWindows() || root.StartsWith(@"\\", StringComparison.Ordinal) ||
            root.Length < 4 || root[1] != ':' || root[2] != '\\' || root[3..].Contains(':') ||
            Path.GetFileName(root) != "update-staging" || !Directory.Exists(root))
            throw new ReleaseValidationException("Use an existing dedicated local update-staging directory.");
        AssertSafe(root);
        var required = checked(manifest.Package.Size + manifest.Files[0].Size + SafetyMargin);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.DriveType != DriveType.Fixed) throw new ReleaseValidationException("Staging requires a fixed local drive.");
        var free = availableSpace is null ? drive.AvailableFreeSpace : availableSpace(root);
        if (free < required) throw new ReleaseValidationException("Insufficient staging disk space.");
        Root = Path.Combine(root, "operation-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(Root) || File.Exists(Root)) throw new ReleaseValidationException("Staging operation collision.");
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new ReleaseValidationException("Current user identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Root).Create(security);
        AssertSafe(Root);
    }

    public FileStream Create(string name)
    {
        AssertSafe(Root);
        var path = Path.Combine(Root, name);
        if (Path.GetFileName(name) != name) throw new ReleaseValidationException("Invalid staging name.");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        _owned.Add(path);
        return stream;
    }

    public void Rename(string from, string to)
    {
        AssertSafe(Root);
        var source = Path.Combine(Root, from);
        var target = Path.Combine(Root, to);
        if (!_owned.Contains(source) || Path.GetFileName(to) != to) throw new ReleaseValidationException("Invalid staging rename.");
        AssertSafe(source);
        File.Move(source, target, overwrite: false);
        _owned.Remove(source);
        _owned.Add(target);
    }

    public bool Cleanup()
    {
        lock (_owned) return CleanupCore();
    }

    private bool CleanupCore()
    {
        if (_cleaned) return true;
        // Reparse/lock/access failures preserve evidence, never follow a new link.
        try
        {
            AssertSafe(Root);
            foreach (var path in _owned) { AssertSafe(path); File.Delete(path); }
            Directory.Delete(Root, recursive: false);
            _cleaned = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ReleaseValidationException) { return false; }
    }

    internal static void AssertSafe(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ReleaseValidationException("Reparse points are not allowed in staging paths.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
