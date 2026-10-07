using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Tests.Models;

public sealed class RelativeRectTests
{
    [Theory]
    [InlineData(-0.20d, -0.10d, 1.50d, 1.40d, 0.00d, 0.00d, 1.00d, 1.00d)]
    [InlineData(0.80d, 0.75d, 0.50d, 0.50d, 0.80d, 0.75d, 0.20d, 0.25d)]
    [InlineData(1.20d, 1.10d, 0.50d, 0.50d, 1.00d, 1.00d, 0.00d, 0.00d)]
    public void Clamp_KeepsRectangleInsideNormalizedWindowBounds(
        double x,
        double y,
        double width,
        double height,
        double expectedX,
        double expectedY,
        double expectedWidth,
        double expectedHeight)
    {
        var clamped = new RelativeRect(x, y, width, height).Clamp();

        Assert.Equal(expectedX, clamped.X, precision: 4);
        Assert.Equal(expectedY, clamped.Y, precision: 4);
        Assert.Equal(expectedWidth, clamped.Width, precision: 4);
        Assert.Equal(expectedHeight, clamped.Height, precision: 4);
    }
}
