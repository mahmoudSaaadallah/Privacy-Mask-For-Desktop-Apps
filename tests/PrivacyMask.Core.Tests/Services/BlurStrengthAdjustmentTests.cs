using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class BlurStrengthAdjustmentTests
{
    [Theory]
    [InlineData(0.00d, 0.05d)]
    [InlineData(0.30d, 0.35d)]
    [InlineData(0.94d, 0.99d)]
    [InlineData(0.98d, 1.00d)]
    public void TryIncrease_AddsOneFivePercentStepAndClamps(double current, double expected)
    {
        var profile = new AppProfile { MaskIntensity = current };

        var changed = BlurStrengthAdjustment.TryIncrease(profile, out var adjusted);

        Assert.True(changed);
        Assert.Equal(expected, adjusted, 2);
        Assert.Equal(expected, profile.MaskIntensity, 2);
    }

    [Fact]
    public void TryIncrease_DoesNotChangeMaximumStrength()
    {
        var profile = new AppProfile { MaskIntensity = MaskIntensityScale.Maximum };

        var changed = BlurStrengthAdjustment.TryIncrease(profile, out var adjusted);

        Assert.False(changed);
        Assert.Equal(MaskIntensityScale.Maximum, adjusted);
        Assert.Equal(MaskIntensityScale.Maximum, profile.MaskIntensity);
    }
}
