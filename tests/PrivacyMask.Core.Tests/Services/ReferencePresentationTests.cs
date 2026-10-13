using System.Globalization;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ReferencePresentationTests
{
    [Fact]
    public void SaveBar_FollowsPendingEditsAndDetailNavigation()
    {
        using var model = SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(), RuntimeMode.Standard, "test", new HashSet<HotkeyAction>());
        Assert.False(model.IsSaveBarVisible);
        model.OpenApplication(model.AppProfiles[0]);
        Assert.True(model.IsSaveBarVisible);
        model.ShowApplicationList();
        Assert.False(model.IsSaveBarVisible);
        model.StartMinimized = !model.StartMinimized;
        Assert.True(model.IsSaveBarVisible);
        model.SetSavedBaseline(model.ToModel());
        Assert.False(model.IsSaveBarVisible);
    }

    [Fact]
    public void Badges_UpdateFromRuntimeWithoutChangingSavedProfileIdentity()
    {
        var profile = new AppProfileViewModel(CustomAppProfileFactory.Create("Editor", "editor"));
        var id = profile.ProfileId;
        profile.UpdateRuntimeStatus(ProfileRuntimeStatus.TargetMissing);
        Assert.Equal("Needs relink", profile.StatusBadgeText);
        Assert.Equal("#FFF3DA", profile.StatusBadgeBackground);
        profile.UpdateRuntimeStatus(ProfileRuntimeStatus.Protected);
        Assert.Equal("Protected now", profile.StatusBadgeText);
        Assert.Equal("#DDF6EB", profile.StatusBadgeBackground);
        Assert.Equal(id, profile.ToModel().ProfileId);
    }

    [Fact]
    public void ActivationRadio_UncheckingCannotOverwriteTheMode()
    {
        var converter = new EnumSelectionConverter();
        Assert.Equal(true, converter.Convert(AppActivationMode.FocusAware, typeof(bool), AppActivationMode.FocusAware, CultureInfo.InvariantCulture));
        Assert.Same(System.Windows.Data.Binding.DoNothing, converter.ConvertBack(false, typeof(AppActivationMode), AppActivationMode.Manual, CultureInfo.InvariantCulture));
        Assert.Equal(AppActivationMode.Manual, converter.ConvertBack(true, typeof(AppActivationMode), AppActivationMode.Manual, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ShortBuiltInNames_DoNotRenamePersistedProfiles()
    {
        var source = new DefaultSettingsFactory().Create().AppProfiles[0];
        var profile = new AppProfileViewModel(source);
        Assert.Equal("WhatsApp", profile.CardDisplayName);
        Assert.Equal(source.DisplayName, profile.ToModel().DisplayName);
    }

    [Fact]
    public void MaskSwatches_AreFrozenAndRepresentEachConfiguredColor()
    {
        var converter = new MaskColorBrushConverter();
        foreach (var option in Enum.GetValues<MaskColorOption>())
        {
            var brush = Assert.IsType<System.Windows.Media.SolidColorBrush>(converter.Convert(option, typeof(System.Windows.Media.Brush), null!, CultureInfo.InvariantCulture));
            Assert.True(brush.IsFrozen);
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(option.ToString()), brush.Color);
        }
    }
}
