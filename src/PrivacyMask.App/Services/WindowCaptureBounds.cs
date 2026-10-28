using PrivacyMask.Core.Models;

namespace PrivacyMask.App.Services;

/// <summary>PrintWindow captures the HWND's local device pixels, not its screen position.</summary>
internal static class WindowCaptureBounds
{
    internal static ScreenRect FromScreen(ScreenRect screenBounds) => new(0, 0, screenBounds.Width, screenBounds.Height);
}
