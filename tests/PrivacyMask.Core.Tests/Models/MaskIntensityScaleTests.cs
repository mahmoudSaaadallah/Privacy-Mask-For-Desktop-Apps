using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Tests.Models;

public sealed class MaskIntensityScaleTests
{
    [Theory]
    [InlineData(0.15d, 0d)]
    [InlineData(0.60d, 0.20d)]
    [InlineData(1.35d, 0.5333333333333333d)]
    [InlineData(2.40d, 1d)]
    public void FromLegacy_PreservesThePreviousRendererProgress(double legacyIntensity, double expected)
    {
        Assert.Equal(expected, MaskIntensityScale.FromLegacy(legacyIntensity), 6);
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0.45d, 0.45d)]
    [InlineData(2d, 1d)]
    public void Clamp_ConstrainsTheSecureSurfaceScale(double intensity, double expected)
    {
        Assert.Equal(expected, MaskIntensityScale.Clamp(intensity));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Clamp_UsesTheDefaultForNonFiniteValues(double intensity)
    {
        Assert.Equal(MaskIntensityScale.Default, MaskIntensityScale.Clamp(intensity));
    }
}
