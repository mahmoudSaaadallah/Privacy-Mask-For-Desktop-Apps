using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Tests.Models;

public sealed class ScreenRectTests
{
    [Fact]
    public void Intersect_ReturnsVisibleOverlap()
    {
        var target = new ScreenRect(0, 0, 500, 400);
        var coveringWindow = new ScreenRect(200, 50, 600, 300);

        var intersection = target.Intersect(coveringWindow);

        Assert.Equal(new ScreenRect(200, 50, 500, 300), intersection);
        Assert.True(target.IntersectsWith(coveringWindow));
    }

    [Fact]
    public void Intersect_ReturnsEmptyRect_WhenWindowsDoNotOverlap()
    {
        var leftWindow = new ScreenRect(0, 0, 300, 300);
        var rightWindow = new ScreenRect(320, 0, 620, 300);

        var intersection = leftWindow.Intersect(rightWindow);

        Assert.True(intersection.IsEmpty);
        Assert.False(leftWindow.IntersectsWith(rightWindow));
    }
}
