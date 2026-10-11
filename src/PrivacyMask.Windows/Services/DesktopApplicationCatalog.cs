using System.Diagnostics;
using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Interop;
using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public sealed class DesktopApplicationCatalog(DesktopWindowInspector windowInspector)
{
    private readonly string _currentProcessName = Process.GetCurrentProcess().ProcessName;

    public IReadOnlyList<DesktopApplicationCandidate> GetAvailableRunningApplications(
        IEnumerable<string> protectedProcessNames)
    {
        var discovered = windowInspector.Capture().CandidateWindows
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.Title))
            .Select(CreateRunningCandidate)
            .Where(candidate => candidate is not null)
            .Cast<DesktopApplicationCandidate>();

        return DesktopApplicationSelectionPolicy.SelectAvailable(
            discovered,
            protectedProcessNames,
            _currentProcessName);
    }

    public bool TryCreateFromForegroundWindow(
        WindowSnapshot snapshot,
        IEnumerable<string> protectedProcessNames,
        out DesktopApplicationCandidate? candidate,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(protectedProcessNames);

        candidate = null;
        failureReason = string.Empty;
        if (snapshot.Handle == nint.Zero
            || !snapshot.IsVisible
            || snapshot.IsMinimized
            || string.IsNullOrWhiteSpace(snapshot.ProcessName))
        {
            failureReason = "Focus a visible desktop application and try again.";
            return false;
        }

        var unavailableReason = DesktopApplicationSelectionPolicy.GetUnavailabilityReason(
            snapshot.ProcessName,
            protectedProcessNames,
            _currentProcessName);
        if (unavailableReason is not null)
        {
            failureReason = unavailableReason;
            return false;
        }

        candidate = CreateRunningCandidate(snapshot) ?? new DesktopApplicationCandidate
        {
            ProcessName = snapshot.ProcessName.Trim(),
            DisplayName = snapshot.ProcessName.Trim(),
            WindowTitle = snapshot.Title,
            IsForeground = true,
            ZOrderIndex = snapshot.ZOrderIndex,
            IsRunning = true,
        };
        return true;
    }

    public bool TryCreateFromExecutable(
        string executablePath,
        IEnumerable<string> protectedProcessNames,
        out DesktopApplicationCandidate? candidate,
        out string failureReason)
    {
        candidate = null;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(executablePath)
            || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(executablePath))
        {
            failureReason = "Choose an existing Windows executable (.exe).";
            return false;
        }

        var resolvedPath = Path.GetFullPath(executablePath);
        var processName = Path.GetFileNameWithoutExtension(resolvedPath);
        var unavailableReason = DesktopApplicationSelectionPolicy.GetUnavailabilityReason(
            processName,
            protectedProcessNames,
            _currentProcessName);
        if (unavailableReason is not null)
        {
            failureReason = unavailableReason;
            return false;
        }

        candidate = new DesktopApplicationCandidate
        {
            ProcessName = processName,
            DisplayName = GetFriendlyName(resolvedPath, processName),
            ExecutablePath = resolvedPath,
            IsRunning = false,
            ZOrderIndex = int.MaxValue,
        };
        return true;
    }

    private static DesktopApplicationCandidate? CreateRunningCandidate(WindowSnapshot snapshot)
    {
        NativeMethods.GetWindowThreadProcessId(snapshot.Handle, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return null;
            }

            return new DesktopApplicationCandidate
            {
                ProcessName = snapshot.ProcessName,
                DisplayName = GetFriendlyName(executablePath, snapshot.ProcessName),
                WindowTitle = snapshot.Title,
                ExecutablePath = executablePath,
                IsForeground = snapshot.IsForeground,
                ZOrderIndex = snapshot.ZOrderIndex,
                IsRunning = true,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetFriendlyName(string executablePath, string fallback)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            var friendlyName = string.IsNullOrWhiteSpace(versionInfo.FileDescription)
                ? versionInfo.ProductName
                : versionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(friendlyName) ? fallback : friendlyName.Trim();
        }
        catch
        {
            return fallback;
        }
    }
}
