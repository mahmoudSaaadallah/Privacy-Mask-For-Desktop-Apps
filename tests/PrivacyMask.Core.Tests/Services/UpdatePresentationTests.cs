using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdatePresentationTests
{
    [Fact]
    public void BlockingApplicationActionRequiresAVerifiedStageAndClearsOnPhaseChanges()
    {
        var status = new UpdateStatusViewModel();
        var block = new UpdateRestartBlock(UpdateRestartBlockKind.VisibleWindows, [new("a", "Example")]);
        status.ShowRestartBlock(block);
        Assert.False(status.CanReviewBlockingApplications);
        status.FinishDownload("ready", ready: true);
        status.ShowRestartBlock(block);
        Assert.True(status.CanReviewBlockingApplications);
        Assert.Contains("Example", status.DownloadText);
        status.ShowRestartBlock(new(UpdateRestartBlockKind.InspectionUnavailable, []));
        Assert.False(status.CanReviewBlockingApplications);
        Assert.DoesNotContain("Example", status.DownloadText);
        status.ShowRestartBlock(block);
        status.BeginInstall();
        Assert.False(status.CanReviewBlockingApplications);
    }

    [Fact]
    public void ExistingWaitCycleReplacesNamesWithoutChangingSettingsOrReadiness()
    {
        var status = new UpdateStatusViewModel();
        status.RequireAutomaticUpdates();
        status.FinishDownload("ready", ready: true);
        status.ShowRestartBlock(new(UpdateRestartBlockKind.VisibleWindows, [new("a", "First app")]));
        status.ShowRestartBlock(new(UpdateRestartBlockKind.VisibleWindows, [new("b", "Second app")]));
        Assert.DoesNotContain("First app", status.DownloadText);
        Assert.Contains("Second app", status.DownloadText);
        Assert.True(status.CanInstall);
        Assert.False(status.CanCancelDownload);
        Assert.False(status.CanDiscardDownload);
        status.ShowAutomaticWait("Waiting for network connectivity.");
        Assert.False(status.CanReviewBlockingApplications);
        Assert.DoesNotContain("Second app", status.DownloadText);
    }
}
