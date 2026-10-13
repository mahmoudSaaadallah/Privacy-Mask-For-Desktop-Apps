using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class OverviewDashboardTests
{
    private static SettingsViewModel Create(RuntimeMode mode = RuntimeMode.Standard) =>
        SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(), mode,
            "C:\\Example\\settings.v1.json", new HashSet<HotkeyAction>());

    [Fact]
    public void RuntimeCounts_DoNotDirtySettingsOrLoseOtherEdits()
    {
        using var model = Create();
        model.AppProfiles[0].UpdateRuntimeStatus(ProfileRuntimeStatus.Protected);
        model.AppProfiles[1].UpdateRuntimeStatus(ProfileRuntimeStatus.PanicProtected);
        Assert.Equal(2, model.ProtectedApplicationCount);
        Assert.False(model.HasPendingChanges);
        model.StartMinimized = !model.StartMinimized;
        model.AppProfiles[0].UpdateRuntimeStatus(ProfileRuntimeStatus.ProtectionPaused);
        model.UpdateRuntimeMode(RuntimeMode.Off);
        Assert.Equal(1, model.ProtectedApplicationCount);
        Assert.True(model.HasPendingChanges);
        Assert.Equal("Resume all", model.GlobalPauseLabel);
        Assert.Equal(RuntimeMode.Standard, model.ToModel().CurrentMode);
    }

    [Fact]
    public void PanicAndBusy_DisableActionsWithoutChangingSavedSettings()
    {
        using var model = Create(RuntimeMode.Panic);
        Assert.False(model.CanToggleGlobalProtection);
        Assert.True(model.CanToggleGlobalPanic);
        Assert.Equal("Unlock panic mask all", model.GlobalPanicLabel);
        model.IsRuntimeActionBusy = true;
        Assert.False(model.CanToggleGlobalPanic);
        model.UpdateRuntimeMode(RuntimeMode.Standard);
        Assert.False(model.CanToggleGlobalProtection);
        model.IsRuntimeActionBusy = false;
        Assert.True(model.CanToggleGlobalProtection);
        Assert.False(model.HasPendingChanges);
    }

    [Fact]
    public void Dashboard_IsBoundedButCountsTheEntireCollection()
    {
        using var model = Create();
        for (var index = 0; index < 12; index++)
            model.AppProfiles.Add(new AppProfileViewModel(CustomAppProfileFactory.Create($"Editor {index}", $"editor{index}")));
        Assert.Equal(14, model.ConfiguredApplicationCount);
        Assert.Equal(6, model.OverviewAppProfiles.Count);
        Assert.Same(model.AppProfiles[0], model.OverviewAppProfiles[0]);
        var removed = model.AppProfiles[0];
        model.AppProfiles.RemoveAt(0);
        Assert.DoesNotContain(removed, model.OverviewAppProfiles);
        removed.UpdateRuntimeStatus(ProfileRuntimeStatus.Protected);
        Assert.Equal(0, model.ProtectedApplicationCount);
        model.AppProfiles.Clear();
        Assert.True(model.HasNoApplications);
        Assert.Empty(model.OverviewAppProfiles);
    }

    [Fact]
    public void QuickStrength_UsesTheSharedEditSession()
    {
        using var model = Create();
        model.OverviewAppProfiles[0].MaskIntensityPercent = 80;
        model.OpenApplication(model.OverviewAppProfiles[0]);
        Assert.Equal(80, model.SelectedApplication!.MaskIntensityPercent);
        Assert.True(model.HasPendingChanges);
        Assert.Equal(0.8, model.ToModel().AppProfiles[0].MaskIntensity);
    }

    [Fact]
    public void Dispose_UnsubscribesRuntimeSummaryUpdates()
    {
        var model = Create();
        var notifications = 0;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.ProtectedApplicationCount)) notifications++; };
        model.Dispose();
        model.AppProfiles[0].UpdateRuntimeStatus(ProfileRuntimeStatus.Protected);
        Assert.Equal(0, notifications);
    }
}
