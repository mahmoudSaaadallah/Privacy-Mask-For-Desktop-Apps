using System.Diagnostics;
using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal static class InstalledLaunchRedirect
{
    internal static bool TryLaunch(string? currentPath, string localAppData, IReadOnlyList<string> arguments,
        Func<string, string?>? resolve = null, Func<ProcessStartInfo, Process?>? start = null)
    {
        if (!OperatingSystem.IsWindows() || currentPath is null || !Path.IsPathFullyQualified(localAppData))
            return false;

        var root = Path.Combine(localAppData, ManifestRules.AppId);
        var installedApp = Path.Combine(root, "app", LauncherBoot.ApplicationFileName);
        if (string.Equals(Path.GetFullPath(currentPath), installedApp, StringComparison.OrdinalIgnoreCase))
            return false;

        try { _ = File.GetAttributes(Path.Combine(root, "managed-install.json")); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }

        var launcher = (resolve ?? ManagedStartupPath.Resolve)(installedApp)
            ?? throw new InvalidOperationException("PrivacyMask could not verify the installed launcher.");
        var launch = new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = root };
        if (arguments.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase)))
            launch.ArgumentList.Add("--minimized");

        using var process = (start ?? Process.Start)(launch)
            ?? throw new IOException("PrivacyMask could not start the installed launcher.");
        return true;
    }
}
