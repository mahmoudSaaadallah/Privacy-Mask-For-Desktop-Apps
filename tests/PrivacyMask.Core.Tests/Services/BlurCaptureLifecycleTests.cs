using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class BlurCaptureLifecycleTests
{
    [Fact]
    public void Complete_ReleasesStaleCaptureSoInvalidatedFrameCanRestart()
    {
        var lifecycle = new BlurCaptureLifecycle();
        Assert.True(lifecycle.TryBegin(out var staleGeneration));

        lifecycle.Invalidate();

        Assert.False(lifecycle.TryBegin(out _));
        Assert.Equal(BlurCaptureCompletion.Stale, lifecycle.Complete(staleGeneration));
        Assert.False(lifecycle.IsCaptureInProgress);
        Assert.True(lifecycle.TryBegin(out var currentGeneration));
        Assert.NotEqual(staleGeneration, currentGeneration);
        Assert.Equal(BlurCaptureCompletion.Current, lifecycle.Complete(currentGeneration));
    }

    [Fact]
    public void Reset_PreventsOldCompletionFromReleasingReplacementCapture()
    {
        var lifecycle = new BlurCaptureLifecycle();
        Assert.True(lifecycle.TryBegin(out var oldGeneration));

        lifecycle.Reset();
        Assert.True(lifecycle.TryBegin(out var replacementGeneration));

        Assert.Equal(BlurCaptureCompletion.Ignored, lifecycle.Complete(oldGeneration));
        Assert.True(lifecycle.IsCaptureInProgress);
        Assert.Equal(BlurCaptureCompletion.Current, lifecycle.Complete(replacementGeneration));
        Assert.False(lifecycle.IsCaptureInProgress);
    }

    [Fact]
    public void Complete_IgnoresDuplicateCompletion()
    {
        var lifecycle = new BlurCaptureLifecycle();
        Assert.True(lifecycle.TryBegin(out var generation));

        Assert.Equal(BlurCaptureCompletion.Current, lifecycle.Complete(generation));
        Assert.Equal(BlurCaptureCompletion.Ignored, lifecycle.Complete(generation));
    }
}
