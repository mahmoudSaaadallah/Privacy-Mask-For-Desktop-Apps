using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class MaskAppearancePolicyTests
{
    [Theory]
    [InlineData(MaskStyle.FrostedGlass)]
    [InlineData(MaskStyle.Pixelate)]
    [InlineData(MaskStyle.SolidRedact)]
    public void Resolve_KeepsEveryProtectiveStyleFullyOpaque(MaskStyle style)
    {
        foreach (var intensity in new[] { 0d, 0.01d, 0.25d, 0.50d, 0.75d, 0.99d, 1d })
        {
            var appearance = MaskAppearancePolicy.Resolve(style, intensity);

            Assert.Equal(MaskAppearancePolicy.SecureOverlayOpacity, appearance.OverlayOpacity);
        }
    }

    [Fact]
    public void Resolve_UsesASmoothMonotonicFrostCurve()
    {
        var light = MaskAppearancePolicy.Resolve(MaskStyle.FrostedGlass, 0d);
        var balanced = MaskAppearancePolicy.Resolve(MaskStyle.FrostedGlass, 0.50d);
        var strong = MaskAppearancePolicy.Resolve(MaskStyle.FrostedGlass, 1d);

        Assert.True(light.HighlightBlend > balanced.HighlightBlend);
        Assert.True(balanced.HighlightBlend > strong.HighlightBlend);
        Assert.True(light.ShadowBlend < balanced.ShadowBlend);
        Assert.True(balanced.ShadowBlend < strong.ShadowBlend);
        Assert.True(light.TextureContrast < balanced.TextureContrast);
        Assert.True(balanced.TextureContrast < strong.TextureContrast);
    }

    [Fact]
    public void Resolve_DoesNotJumpToADifferentAppearanceAtMaximum()
    {
        var nearMaximum = MaskAppearancePolicy.Resolve(MaskStyle.FrostedGlass, 0.99d);
        var maximum = MaskAppearancePolicy.Resolve(MaskStyle.FrostedGlass, 1d);

        Assert.InRange(Math.Abs(maximum.HighlightBlend - nearMaximum.HighlightBlend), 0d, 0.001d);
        Assert.InRange(Math.Abs(maximum.ShadowBlend - nearMaximum.ShadowBlend), 0d, 0.001d);
        Assert.InRange(Math.Abs(maximum.TextureContrast - nearMaximum.TextureContrast), 0d, 0.001d);
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(2d, 1d)]
    [InlineData(double.NaN, MaskIntensityScale.Default)]
    public void Resolve_NormalizesInvalidIntensity(double intensity, double expected)
    {
        var appearance = MaskAppearancePolicy.Resolve(MaskStyle.FrostedGlass, intensity);

        Assert.Equal(expected, appearance.Intensity);
    }
}
