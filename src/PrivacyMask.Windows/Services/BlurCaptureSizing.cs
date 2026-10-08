using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public static class BlurCaptureSizing
{
    public const int MaximumLongEdge = 1600;

    public static BlurCaptureSize Calculate(int sourceWidth, int sourceHeight, double downsampleFactor)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return default;
        }

        var safeFactor = double.IsFinite(downsampleFactor)
            ? double.Clamp(downsampleFactor, 1d, 64d)
            : 1d;
        var longEdgeScale = Math.Min(1d, MaximumLongEdge / (double)Math.Max(sourceWidth, sourceHeight));
        var scale = longEdgeScale / safeFactor;

        return new BlurCaptureSize(
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }
}
