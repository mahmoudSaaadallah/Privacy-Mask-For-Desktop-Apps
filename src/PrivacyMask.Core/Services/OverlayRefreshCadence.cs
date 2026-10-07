using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class OverlayRefreshCadence
{
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(500);

    public static TimeSpan Select(RuntimeMode mode, bool hasCandidateWindows)
    {
        return mode != RuntimeMode.Off && hasCandidateWindows
            ? ActiveInterval
            : IdleInterval;
    }
}
