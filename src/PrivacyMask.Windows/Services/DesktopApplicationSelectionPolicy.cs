using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public static class DesktopApplicationSelectionPolicy
{
    private static readonly HashSet<string> BlockedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost",
        "consent",
        "CredentialUIBroker",
        "csrss",
        "dwm",
        "explorer",
        "LockApp",
        "LogonUI",
        "lsass",
        "MsMpEng",
        "NisSrv",
        "SearchHost",
        "SecHealthUI",
        "SecurityHealthHost",
        "SecurityHealthService",
        "SecurityHealthSystray",
        "services",
        "ShellExperienceHost",
        "sihost",
        "smartscreen",
        "StartMenuExperienceHost",
        "Taskmgr",
        "TextInputHost",
        "wininit",
        "winlogon",
    };

    public static IReadOnlyList<DesktopApplicationCandidate> SelectAvailable(
        IEnumerable<DesktopApplicationCandidate> candidates,
        IEnumerable<string> protectedProcessNames,
        string currentProcessName)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(protectedProcessNames);

        var unavailableProcessNames = protectedProcessNames
            .Where(processName => !string.IsNullOrWhiteSpace(processName))
            .Select(processName => processName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(currentProcessName))
        {
            unavailableProcessNames.Add(currentProcessName.Trim());
        }

        return candidates
            .Where(candidate => IsSelectable(candidate.ProcessName, candidate.ExecutablePath, unavailableProcessNames))
            .GroupBy(candidate => candidate.ProcessName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.IsForeground)
                .ThenBy(candidate => candidate.ZOrderIndex)
                .First())
            .OrderBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? GetUnavailabilityReason(
        string processName,
        IEnumerable<string> protectedProcessNames,
        string currentProcessName)
    {
        ArgumentNullException.ThrowIfNull(protectedProcessNames);

        if (string.IsNullOrWhiteSpace(processName))
        {
            return "The executable does not have a valid process name.";
        }

        var normalizedProcessName = processName.Trim();
        if (BlockedProcessNames.Contains(normalizedProcessName)
            || string.Equals(normalizedProcessName, currentProcessName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "PrivacyMask cannot target its own process or this Windows system surface.";
        }

        if (protectedProcessNames.Any(existing =>
            string.Equals(existing?.Trim(), normalizedProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            return "This application is already protected by an existing profile.";
        }

        return null;
    }

    private static bool IsSelectable(
        string processName,
        string executablePath,
        IReadOnlySet<string> unavailableProcessNames)
    {
        return !string.IsNullOrWhiteSpace(processName)
            && !string.IsNullOrWhiteSpace(executablePath)
            && !BlockedProcessNames.Contains(processName.Trim())
            && !unavailableProcessNames.Contains(processName.Trim());
    }
}
