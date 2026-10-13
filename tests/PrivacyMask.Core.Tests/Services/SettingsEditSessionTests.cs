using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class SettingsEditSessionTests
{
    private static SettingsViewModel Create()
    {
        return SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(),
            RuntimeMode.Standard, "test", new HashSet<HotkeyAction>());
    }

    [Fact]
    public void NavigationAndRuntimeStatus_DoNotCreatePendingEdits()
    {
        using var session = Create();
        foreach (var page in Enum.GetValues<DesktopPage>()) session.SelectedPage = page;
        session.AppProfiles[0].UpdateRuntimeStatus(ProfileRuntimeStatus.NotRunning);
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void RevertingAValue_ClearsPendingChangesAcrossPages()
    {
        using var session = Create();
        var original = session.StartMinimized;
        session.StartMinimized = !original;
        session.SelectedPage = DesktopPage.Applications;
        Assert.True(session.HasPendingChanges);
        session.StartMinimized = original;
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void NestedZoneEdits_AreTrackedIncludingReplacementZones()
    {
        using var session = Create();
        session.AppProfiles[0].ApplySelectedPreset();
        Assert.False(session.HasPendingChanges);
        session.AppProfiles[0].Zones[0].RevealOnHover = false;
        Assert.True(session.HasPendingChanges);
        session.SetSavedBaseline(session.ToModel());
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void ProfileAdditionRemovalAndRelink_AreTracked()
    {
        using var session = Create();
        var custom = new AppProfileViewModel(CustomAppProfileFactory.Create("Notes", "Notes"));
        session.AppProfiles.Add(custom);
        Assert.True(session.HasPendingChanges);
        session.AppProfiles.Remove(custom);
        Assert.False(session.HasPendingChanges);
        session.AppProfiles.Add(custom);
        session.SetSavedBaseline(session.ToModel());
        custom.Relink("NewNotes", "C:\\Apps\\NewNotes.exe");
        Assert.True(session.HasPendingChanges);
    }

    [Fact]
    public void HotkeyAndPinChanges_AreTracked()
    {
        using var session = Create();
        session.Hotkeys[0].Enabled = false;
        Assert.True(session.HasPendingChanges);
        session.SetSavedBaseline(session.ToModel());
        session.SetPanicPin(PanicPinService.Create("1357"));
        Assert.True(session.HasPendingChanges);
    }

    [Fact]
    public void ExternalSavedUpdate_DoesNotEraseUnrelatedPendingEdits()
    {
        using var session = Create();
        var saved = session.ToModel();
        session.StartMinimized = false;
        saved.AppProfiles[0].MaskIntensity = 0.25;
        session.AppProfiles[0].MaskIntensity = 0.25;
        session.SetSavedBaseline(saved);
        Assert.True(session.HasPendingChanges);
        Assert.False(session.StartMinimized);
    }
}
