using PrivacyMask.Rendering.Benchmark;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RendererTimingTests
{
    [Fact]
    public void ResetExcludesWarmupAndDoesNotReportEmptyAsZeroLatency()
    {
        var histogram = new TimingHistogram();
        histogram.Add(1500);
        histogram.Reset();
        Assert.Equal(new TimingSnapshot(0, null, null, null, false), histogram.Snapshot());
        histogram.Add(0.2);
        histogram.Add(1.8);
        Assert.Equal(new TimingSnapshot(2, 1, 1.8, 2, false), histogram.Snapshot());
    }

    [Fact]
    public void PercentileUsesNearestRankAndReportsOverflowWithoutCappingMaximum()
    {
        var histogram = new TimingHistogram();
        for (var i = 0; i < 19; i++) histogram.Add(0.5);
        histogram.Add(5000);
        Assert.Equal(1, histogram.Snapshot().P95UpperBoundMilliseconds);
        histogram.Add(6000);
        Assert.True(histogram.Snapshot().P95Overflow);
        Assert.Null(histogram.Snapshot().P95UpperBoundMilliseconds);
        Assert.Equal(6000, histogram.Snapshot().MaximumMilliseconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidMeasurements(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimingHistogram().Add(value));
    }
}
