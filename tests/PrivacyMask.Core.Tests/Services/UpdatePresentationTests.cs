using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Updates;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdatePresentationTests
{
    [Fact]
    public void ProgressIsNotProofOfAReadyOrInstalledUpdate()
    {
        var status = new UpdateStatusViewModel();
        status.Begin();
        Assert.Equal("Checking for updates", status.StatusHeading);
        status.BeginDownload();
        status.DownloadProgress(new(UpdateDownloadPhase.Downloading, 512, 1024));
        Assert.Equal(50, status.DownloadPercent);
        Assert.Contains("Downloading:", status.StatusDetail);
        status.DownloadProgress(new(UpdateDownloadPhase.Verifying, 1024, 1024));
        Assert.Equal("Verifying update package", status.StatusHeading);
        status.DownloadProgress(new(UpdateDownloadPhase.Extracting, 1024, 1024));
        Assert.Equal("Verifying application", status.StatusHeading);
        status.DownloadProgress(new(UpdateDownloadPhase.Ready, 1024, 1024));
        Assert.False(status.HasVerifiedDownload);
        Assert.False(status.CanInstall);
        status.FinishDownload("Verified, not installed", ready: true);
        Assert.Equal("Verified update ready", status.StatusHeading);
        status.BeginInstall();
        Assert.Equal("Preparing installation", status.StatusHeading);
        status.ShowRestarting();
        Assert.Equal("Restarting to apply update", status.StatusHeading);
        Assert.Contains("success is not yet confirmed", status.StatusDetail);
    }

    [Fact]
    public void FreshCheckClearsOldFailureAndOnlyRealRetryDeadlineIsShown()
    {
        var status = new UpdateStatusViewModel();
        status.FinishDownload("old failure");
        Assert.Equal("Update attempt did not finish", status.StatusHeading);
        Assert.Empty(status.RetryText);
        var retry = DateTimeOffset.UtcNow.AddHours(2);
        status.Complete(new(UpdateCheckStatus.RateLimited, RetryAt: retry), UpdateReleaseChannel.Preview, DateTimeOffset.UtcNow);
        Assert.Contains(retry.ToLocalTime().ToString("g"), status.RetryText);
        Assert.DoesNotContain("old failure", status.StatusDetail);
        status.Begin();
        Assert.Empty(status.RetryText);
        Assert.False(status.HasDownloadStatus);
        Assert.Equal("Checking for updates", status.StatusHeading);
        status.Complete(new(UpdateCheckStatus.UpToDate), UpdateReleaseChannel.Preview, DateTimeOffset.UtcNow);
        Assert.Equal("Up to date", status.StatusHeading);
    }

    [Fact]
    public void OfflineWaitIsVisibleBeforeAnyDownloadAndRepairOverridesReady()
    {
        var status = new UpdateStatusViewModel();
        status.ShowAutomaticWait("Waiting for network connectivity.");
        Assert.Equal("Update waiting", status.StatusHeading);
        Assert.Contains("network", status.StatusDetail);
        Assert.False(status.HasVerifiedDownload);
        status.FinishDownload("ready", ready: true);
        status.FinishInstall("Recovery required", repairRequired: true);
        Assert.Equal("Update recovery needs attention", status.StatusHeading);
        Assert.False(status.CanInstall);
    }

    [Fact]
    public void RequiredDownloadFailureRetainsKnownServerDeadlineAndRepairDoesNotClaimUpToDate()
    {
        var status = new UpdateStatusViewModel();
        status.RequireAutomaticUpdates();
        var now = DateTimeOffset.UtcNow;
        var retry = now.AddHours(2);
        status.Complete(new(UpdateCheckStatus.RateLimited, RetryAt: retry), status.RequiredChannel, now);
        status.FinishDownload("Download failed; no application was installed.");
        Assert.Contains(retry.ToLocalTime().ToString("g"), status.RetryText);
        Assert.Equal("Update attempt did not finish", status.StatusHeading);
        status.FinishDownload("Cleanup requires attention", cleanupIncomplete: true);
        status.Begin();
        status.Complete(new(UpdateCheckStatus.UpToDate), status.RequiredChannel, now);
        Assert.Equal("Update recovery needs attention", status.StatusHeading);
        Assert.Equal("Cleanup requires attention Some staging files could not be removed. Use View releases; no installed files were changed.", status.StatusDetail);
    }

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
