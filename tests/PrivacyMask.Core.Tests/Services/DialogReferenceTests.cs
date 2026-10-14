using System.Text.Json;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Models;

namespace PrivacyMask.Core.Tests.Services;

public sealed class DialogReferenceTests
{
    private static SettingsViewModel Create() => SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(), RuntimeMode.Standard, "synthetic", new HashSet<HotkeyAction>());

    [Fact]
    public void AdvancedNavigation_DoesNotChangeSettingsAndRequiresSelectedProfile()
    {
        using var session = Create();
        var before = JsonSerializer.Serialize(session.ToModel());
        session.ShowAdvancedZones();
        Assert.False(session.IsAdvancedZonesPage);
        session.OpenApplication(session.AppProfiles[0]);
        Assert.True(session.IsPrimaryApplicationDetailsPage);
        session.ShowAdvancedZones();
        Assert.True(session.IsAdvancedZonesPage);
        Assert.False(session.IsPrimaryApplicationDetailsPage);
        Assert.Equal(before, JsonSerializer.Serialize(session.ToModel()));
        Assert.False(session.HasPendingChanges);
        session.ShowPrimaryApplicationDetails();
        Assert.True(session.IsPrimaryApplicationDetailsPage);
    }

    [Fact]
    public void AdvancedEdits_UseTheSameDirtySessionAndNormalizedModel()
    {
        using var session = Create();
        session.OpenApplication(session.AppProfiles[0]);
        session.ShowAdvancedZones();
        var zone = session.SelectedApplication!.Zones[0];
        zone.Width = .75;
        zone.SurfaceIntensityPercent = 35;
        zone.RevealOnHover = false;
        Assert.True(session.HasPendingChanges);
        var saved = session.ToModel().AppProfiles[0].Zones[0];
        Assert.Equal(.75, saved.RelativeRect.Width);
        Assert.Equal(.35, saved.Strength);
        Assert.False(saved.Behavior.HasFlag(ZoneBehavior.RevealOnHover));
        session.SetSavedBaseline(session.ToModel());
        Assert.False(session.HasPendingChanges);
        Assert.True(session.IsAdvancedZonesPage);
    }

    [Fact]
    public void SwitchingProfileOrReturningToList_ExitsAdvancedView()
    {
        using var session = Create();
        session.OpenApplication(session.AppProfiles[0]);
        session.ShowAdvancedZones();
        session.OpenApplication(session.AppProfiles[1]);
        Assert.True(session.IsPrimaryApplicationDetailsPage);
        session.ShowAdvancedZones();
        session.ShowApplicationList();
        Assert.False(session.IsAdvancedZonesPage);
        Assert.True(session.IsApplicationListPage);
    }

    [Fact]
    public void CandidateExecutableLabel_IsCompactWithoutChangingTarget()
    {
        var candidate = new DesktopApplicationCandidate { ProcessName = "editor", DisplayName = "Editor", ExecutablePath = "C:\\Synthetic\\editor.exe" };
        var row = new ApplicationCandidateViewModel(candidate);
        Assert.Equal("editor.exe", row.ExecutableName);
        Assert.Same(candidate, row.Candidate);
    }
}
