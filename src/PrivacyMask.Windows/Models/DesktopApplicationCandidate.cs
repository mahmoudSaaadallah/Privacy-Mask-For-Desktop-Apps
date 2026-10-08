namespace PrivacyMask.Windows.Models;

public sealed class DesktopApplicationCandidate
{
    public string ProcessName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string WindowTitle { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public bool IsForeground { get; init; }

    public int ZOrderIndex { get; init; }

    public bool IsRunning { get; init; }
}
