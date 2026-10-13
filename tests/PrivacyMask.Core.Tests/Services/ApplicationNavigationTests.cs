using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ApplicationNavigationTests
{
    private static SettingsViewModel Create()
    {
        var settings = new DefaultSettingsFactory().Create();
        settings.AppProfiles.Add(CustomAppProfileFactory.Create("Editor", "Notepad"));
        return SettingsViewModel.FromModel(settings, RuntimeMode.Standard, "test", new HashSet<HotkeyAction>());
    }

    [Fact]
    public void Search_FiltersReferencesWithoutChangingSavedProfiles()
    {
        using var model = Create();
        model.ApplicationSearch = "NOTE";
        Assert.Same(model.AppProfiles[2], Assert.Single(model.FilteredAppProfiles));
        Assert.Equal(3, model.ToModel().AppProfiles.Count);
        Assert.False(model.HasPendingChanges);
        model.ApplicationSearch = "No such application";
        Assert.True(model.HasNoMatchingApplications);
        Assert.Equal(3, model.AppProfiles.Count);
    }

    [Fact]
    public void DetailsAndBack_RetainTheEditedProfile()
    {
        using var model = Create();
        var profile = model.AppProfiles[2];
        model.OpenApplication(profile);
        Assert.True(model.IsApplicationDetailsPage);
        profile.MaskIntensity = 0.35;
        model.ShowApplicationList();
        Assert.True(model.IsApplicationListPage);
        model.OpenApplication(profile);
        Assert.Equal(0.35, model.SelectedApplication!.MaskIntensity);
        Assert.True(model.HasPendingChanges);
    }

    [Fact]
    public void RemovingSelectedProfile_ReturnsToList()
    {
        using var model = Create();
        model.OpenApplication(model.AppProfiles[2]);
        model.AppProfiles.RemoveAt(2);
        Assert.Null(model.SelectedApplication);
        Assert.True(model.IsApplicationListPage);
    }

    [Fact]
    public void Relink_UpdatesFilteredProcessAndPreservesPrivacySettings()
    {
        using var model = Create();
        var profile = model.AppProfiles[2];
        profile.MaskIntensity = 0.4;
        profile.HoverRevealWidthPixels = 800;
        profile.PrimaryMaskStyle = MaskStyle.WetGlass;
        model.ApplicationSearch = "NewEditor";
        Assert.Empty(model.FilteredAppProfiles);
        profile.Relink("NewEditor", "C:\\Apps\\NewEditor.exe");
        Assert.Same(profile, Assert.Single(model.FilteredAppProfiles));
        Assert.Equal(0.4, profile.MaskIntensity);
        Assert.Equal(800, profile.HoverRevealWidthPixels);
        Assert.Equal(MaskStyle.WetGlass, profile.PrimaryMaskStyle);
    }

    [Fact]
    public void HoverSwitch_UpdatesEveryZoneAndTracksTheEdit()
    {
        using var model = Create();
        var profile = model.AppProfiles[2];
        profile.Zones.Add(new PrivacyZoneViewModel(new PrivacyZone
        {
            ZoneId = "secondary",
            DisplayName = "Secondary zone",
            Behavior = ZoneBehavior.RevealOnHover,
        }));
        model.SetSavedBaseline(model.ToModel());
        profile.HoverRevealEnabled = false;
        Assert.All(profile.Zones, zone => Assert.False(zone.RevealOnHover));
        Assert.True(model.HasPendingChanges);
        profile.HoverRevealEnabled = true;
        Assert.All(profile.Zones, zone => Assert.True(zone.RevealOnHover));
        Assert.False(model.HasPendingChanges);
    }

    [Fact]
    public void Rename_RefreshesSearchWithoutLosingDetailSelection()
    {
        using var model = Create();
        var profile = model.AppProfiles[2];
        model.OpenApplication(profile);
        model.ApplicationSearch = "Renamed";
        Assert.Empty(model.FilteredAppProfiles);
        profile.DisplayName = "Renamed editor";
        Assert.Same(profile, Assert.Single(model.FilteredAppProfiles));
        Assert.Same(profile, model.SelectedApplication);
    }
}
