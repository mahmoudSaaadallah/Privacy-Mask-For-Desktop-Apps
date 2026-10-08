using System;

namespace PrivacyMask.Core.Models;

public static class MaskIntensityScale
{
    public const double Minimum = 0d;
    public const double Maximum = 1d;
    public const double Default = 0.55d;

    private const double LegacyNormalizationOffset = 0.15d;
    private const double LegacyNormalizationRange = 2.25d;

    public static double Clamp(double intensity)
    {
        return double.IsFinite(intensity)
            ? double.Clamp(intensity, Minimum, Maximum)
            : Default;
    }

    public static double FromLegacy(double legacyIntensity)
    {
        if (!double.IsFinite(legacyIntensity))
        {
            return Default;
        }

        return Clamp((legacyIntensity - LegacyNormalizationOffset) / LegacyNormalizationRange);
    }
}
