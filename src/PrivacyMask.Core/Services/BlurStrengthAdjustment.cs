using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class BlurStrengthAdjustment
{
    public const double Step = 0.05d;

    public static bool TryIncrease(AppProfile profile, out double adjustedStrength)
    {
        var current = MaskIntensityScale.Clamp(profile.MaskIntensity);
        adjustedStrength = Math.Min(
            MaskIntensityScale.Maximum,
            Math.Round(current + Step, 2, MidpointRounding.AwayFromZero));

        if (adjustedStrength <= current)
        {
            adjustedStrength = current;
            return false;
        }

        profile.MaskIntensity = adjustedStrength;
        return true;
    }

    public static bool TryDecrease(AppProfile profile, out double adjustedStrength)
    {
        var current = MaskIntensityScale.Clamp(profile.MaskIntensity);
        adjustedStrength = Math.Max(
            MaskIntensityScale.Minimum,
            Math.Round(current - Step, 2, MidpointRounding.AwayFromZero));

        if (adjustedStrength >= current)
        {
            adjustedStrength = current;
            return false;
        }

        profile.MaskIntensity = adjustedStrength;
        return true;
    }
}
