using PrivacyMask.Windows.Models;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class DesktopApplicationSelectionPolicyTests
{
    [Fact]
    public void SelectAvailable_FiltersUnsafeExistingAndCurrentProcesses()
    {
        var candidates = new[]
        {
            Candidate("Notepad", "Notepad", 3),
            Candidate("Telegram", "Telegram", 2),
            Candidate("PrivacyMask.App", "PrivacyMask", 1),
            Candidate("explorer", "File Explorer", 4),
        };

        var available = DesktopApplicationSelectionPolicy.SelectAvailable(
            candidates,
            ["Telegram"],
            "PrivacyMask.App");

        var selected = Assert.Single(available);
        Assert.Equal("Notepad", selected.ProcessName);
    }

    [Fact]
    public void SelectAvailable_GroupsWindowsByProcessAndPrefersForegroundWindow()
    {
        var background = Candidate("Code", "Visual Studio Code", 1, "First project");
        var foreground = Candidate("code", "Visual Studio Code", 7, "Current project", isForeground: true);

        var available = DesktopApplicationSelectionPolicy.SelectAvailable(
            [background, foreground],
            [],
            "PrivacyMask.App");

        Assert.Same(foreground, Assert.Single(available));
    }

    [Fact]
    public void GetUnavailabilityReason_ExplainsDuplicateExecutable()
    {
        var reason = DesktopApplicationSelectionPolicy.GetUnavailabilityReason(
            "notepad",
            ["Notepad"],
            "PrivacyMask.App");

        Assert.Contains("already protected", reason);
    }

    private static DesktopApplicationCandidate Candidate(
        string processName,
        string displayName,
        int zOrderIndex,
        string windowTitle = "Window",
        bool isForeground = false)
    {
        return new DesktopApplicationCandidate
        {
            ProcessName = processName,
            DisplayName = displayName,
            WindowTitle = windowTitle,
            ExecutablePath = $"C:\\Apps\\{processName}.exe",
            IsForeground = isForeground,
            IsRunning = true,
            ZOrderIndex = zOrderIndex,
        };
    }
}
