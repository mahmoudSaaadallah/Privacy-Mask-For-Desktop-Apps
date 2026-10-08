using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public static class MaskAppearancePolicy
{
    public const double SecureOverlayOpacity = 1d;

    public static MaskSurfaceAppearance Resolve(MaskStyle style, double intensity)
    {
        var normalized = MaskIntensityScale.Clamp(intensity);
        var smoothed = SmoothStep(normalized);

        return style switch
        {
            MaskStyle.Pixelate => new MaskSurfaceAppearance(
                normalized,
                smoothed,
                SecureOverlayOpacity,
                Lerp(0.58d, 0.40d, smoothed),
                Lerp(0.30d, 0.18d, smoothed),
                Lerp(0.22d, 0.52d, smoothed),
                Lerp(0.18d, 0.48d, smoothed),
                Lerp(1.35d, 0.60d, smoothed)),
            MaskStyle.SolidRedact => new MaskSurfaceAppearance(
                normalized,
                smoothed,
                SecureOverlayOpacity,
                Lerp(0.12d, 0.04d, smoothed),
                Lerp(0.12d, 0.04d, smoothed),
                Lerp(0.12d, 0.30d, smoothed),
                0d,
                1d),
            _ => new MaskSurfaceAppearance(
                normalized,
                smoothed,
                SecureOverlayOpacity,
                Lerp(0.72d, 0.48d, smoothed),
                Lerp(0.46d, 0.26d, smoothed),
                Lerp(0.18d, 0.50d, smoothed),
                Lerp(0.035d, 0.15d, smoothed),
                Lerp(1.25d, 0.80d, smoothed)),
        };
    }

    private static double SmoothStep(double value)
    {
        return value * value * (3d - (2d * value));
    }

    private static double Lerp(double start, double end, double progress)
    {
        return start + ((end - start) * progress);
    }
}
