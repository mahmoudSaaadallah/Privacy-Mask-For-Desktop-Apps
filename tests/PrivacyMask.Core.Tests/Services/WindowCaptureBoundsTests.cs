using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Tests.Services;

public sealed class WindowCaptureBoundsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 300)]
    [InlineData(-1920, -200)]
    public void MovingAcrossScreenCoordinatesKeepsWindowLocalCaptureIdentity(int left, int top)
    {
        Assert.Equal(new ScreenRect(0, 0, 900, 700),
            WindowCaptureBounds.FromScreen(new(left, top, left + 900, top + 700)));
    }

    [Fact]
    public void ResizeStillChangesCaptureIdentity()
    {
        var before = WindowCaptureBounds.FromScreen(new(100, 100, 1000, 800));
        Assert.NotEqual(before, WindowCaptureBounds.FromScreen(new(100, 100, 1001, 800)));
        Assert.NotEqual(before, WindowCaptureBounds.FromScreen(new(100, 100, 1000, 801)));
    }
}
