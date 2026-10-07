using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class WindowSizePolicyTests
{
    [Fact]
    public void FitToWorkArea_PreservesDesiredSizeWhenItFits()
    {
        var result = WindowSizePolicy.FitToWorkArea(
            workAreaWidth: 1920d,
            workAreaHeight: 1040d,
            desiredWidth: 1460d,
            desiredHeight: 920d,
            configuredMinWidth: 1180d,
            configuredMinHeight: 760d);

        Assert.Equal(1460d, result.Width);
        Assert.Equal(920d, result.Height);
        Assert.Equal(1180d, result.MinWidth);
        Assert.Equal(760d, result.MinHeight);
    }

    [Fact]
    public void FitToWorkArea_ClampsWindowAndMinimumsToTheAvailableArea()
    {
        var result = WindowSizePolicy.FitToWorkArea(
            workAreaWidth: 1366d,
            workAreaHeight: 728d,
            desiredWidth: 1460d,
            desiredHeight: 920d,
            configuredMinWidth: 1180d,
            configuredMinHeight: 760d);

        Assert.Equal(1334d, result.Width);
        Assert.Equal(696d, result.Height);
        Assert.Equal(1180d, result.MinWidth);
        Assert.Equal(696d, result.MinHeight);
    }

    [Fact]
    public void FitToWorkArea_HandlesVerySmallWorkAreasWithoutInvalidDimensions()
    {
        var result = WindowSizePolicy.FitToWorkArea(
            workAreaWidth: 20d,
            workAreaHeight: 20d,
            desiredWidth: 760d,
            desiredHeight: 560d,
            configuredMinWidth: 720d,
            configuredMinHeight: 520d);

        Assert.Equal(1d, result.Width);
        Assert.Equal(1d, result.Height);
        Assert.Equal(1d, result.MinWidth);
        Assert.Equal(1d, result.MinHeight);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FitToWorkArea_RejectsInvalidWorkAreaDimensions(double invalidDimension)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowSizePolicy.FitToWorkArea(
            invalidDimension,
            workAreaHeight: 1080d,
            desiredWidth: 760d,
            desiredHeight: 560d,
            configuredMinWidth: 720d,
            configuredMinHeight: 520d));
    }
}
