namespace PrivacyMask.Core.Models;

public sealed class WindowSnapshot
{
    public nint Handle { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public ScreenRect Bounds { get; init; }

    public int ZOrderIndex { get; init; }

    public bool IsVisible { get; init; }

    public bool IsForeground { get; init; }

    public bool IsMinimized { get; init; }
}
