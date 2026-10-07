using PrivacyMask.Core.Models;

namespace PrivacyMask.Windows.Models;

public readonly record struct DesktopWindowSnapshot(
    nint Handle,
    ScreenRect Bounds,
    int ZOrderIndex,
    bool IsVisible,
    bool IsMinimized);
