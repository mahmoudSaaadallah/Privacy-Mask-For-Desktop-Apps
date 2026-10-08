using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class BlurCaptureSizingTests
{
    [Theory]
    [InlineData(800, 1080, 1d, 800, 1080)]
    [InlineData(800, 1080, 8d, 100, 135)]
    [InlineData(800, 1080, 32d, 25, 34)]
    public void Calculate_MapsBlurFactorToSmallerFrames(
        int sourceWidth,
        int sourceHeight,
        double factor,
        int expectedWidth,
        int expectedHeight)
    {
        var result = BlurCaptureSizing.Calculate(sourceWidth, sourceHeight, factor);

        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }

    [Fact]
    public void Calculate_BoundsLowStrengthMemoryOnLargeWindows()
    {
        var result = BlurCaptureSizing.Calculate(3840, 2160, 1d);

        Assert.Equal(1600, result.Width);
        Assert.Equal(900, result.Height);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void Calculate_RejectsInvalidSourceBounds(int width, int height)
    {
        Assert.True(BlurCaptureSizing.Calculate(width, height, 4d).Equals(default));
    }
}
