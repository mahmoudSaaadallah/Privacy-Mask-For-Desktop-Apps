using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class BlurCaptureHealthTests
{
    [Fact]
    public void RecordFailure_DisablesCaptureAtConfiguredThreshold()
    {
        var health = new BlurCaptureHealth(maximumConsecutiveFailures: 3);

        Assert.False(health.RecordFailure());
        Assert.False(health.RecordFailure());
        Assert.True(health.RecordFailure());
        Assert.True(health.IsDisabled);
    }

    [Fact]
    public void RecordSuccess_ResetsConsecutiveFailureCount()
    {
        var health = new BlurCaptureHealth(maximumConsecutiveFailures: 2);

        Assert.False(health.RecordFailure());
        health.RecordSuccess();

        Assert.False(health.RecordFailure());
        Assert.False(health.IsDisabled);
    }

    [Fact]
    public void RecordTimeout_DisablesCaptureImmediately()
    {
        var health = new BlurCaptureHealth();

        health.RecordTimeout();

        Assert.True(health.IsDisabled);
        Assert.True(health.RecordFailure());
    }
}
