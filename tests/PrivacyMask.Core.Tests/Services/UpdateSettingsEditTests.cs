using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateSettingsEditTests
{
    [Fact]
    public void PreferencesParticipateInTheSharedSaveAndDiscardBaseline()
    {
        var saved = new DefaultSettingsFactory().Create();
        using var session = SettingsViewModel.FromModel(saved, RuntimeMode.Standard, "test", new HashSet<HotkeyAction>());
        session.AutomaticUpdateChecks = !saved.AutomaticUpdateChecks;
        session.UpdateChannel = saved.UpdateChannel == UpdateReleaseChannel.Preview ? UpdateReleaseChannel.Stable : UpdateReleaseChannel.Preview;
        Assert.True(session.HasPendingChanges);
        var updated = session.ToModel();
        Assert.Equal(session.UpdateChannel, updated.UpdateChannel);
        Assert.Equal(session.AutomaticUpdateChecks, updated.AutomaticUpdateChecks);
        session.SetSavedBaseline(updated);
        Assert.False(session.HasPendingChanges);
        Assert.Equal(updated.UpdateChannel, AppSettingsCloner.Clone(updated).UpdateChannel);
    }

    [Fact]
    public void RevertingPreferencesClearsEditsButStatusUpdatesNeverCreateThem()
    {
        var saved = new DefaultSettingsFactory().Create();
        var status = new UpdateStatusViewModel();
        using var session = SettingsViewModel.FromModel(saved, RuntimeMode.Standard, "test", new HashSet<HotkeyAction>(), status);
        session.AutomaticUpdateChecks = !saved.AutomaticUpdateChecks;
        session.AutomaticUpdateChecks = saved.AutomaticUpdateChecks;
        status.Begin();
        status.Complete(new(UpdateCheckStatus.NotConfigured), saved.UpdateChannel, DateTimeOffset.UtcNow);
        Assert.False(session.HasPendingChanges);
        Assert.Same(status, session.Updates);
    }

    [Fact]
    public void OtherChannelFailureDoesNotReuseSuccessfulChannelTimestamp()
    {
        var status = new UpdateStatusViewModel();
        status.Complete(new(UpdateCheckStatus.UpToDate), UpdateReleaseChannel.Preview, DateTimeOffset.UtcNow);
        status.Complete(new(UpdateCheckStatus.UnableToCheck), UpdateReleaseChannel.Stable, DateTimeOffset.UtcNow);
        Assert.Contains("never", status.LastCheckText);
        status.Complete(new(UpdateCheckStatus.UnableToCheck), UpdateReleaseChannel.Preview, DateTimeOffset.UtcNow);
        Assert.DoesNotContain("never", status.LastCheckText);
    }
}
