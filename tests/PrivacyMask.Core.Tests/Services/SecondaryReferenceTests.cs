using System.Text.Json;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class SecondaryReferenceTests
{
    private static SettingsViewModel Create() => SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(), RuntimeMode.Standard, "synthetic", new HashSet<HotkeyAction>());

    [Fact]
    public void ShortcutDisplayOrder_DoesNotReorderSavedBindings()
    {
        using var model = Create();
        var original = model.ToModel().GlobalHotkeys.Select(key => key.Action).ToArray();
        Assert.Equal(new[] { HotkeyAction.ToggleProtection, HotkeyAction.PanicHideAll, HotkeyAction.IncreaseBlurStrength, HotkeyAction.DecreaseBlurStrength, HotkeyAction.TemporaryRevealHold, HotkeyAction.OpenSettings }, model.ShortcutRows.Select(key => key.Action));
        Assert.Equal(original, model.ToModel().GlobalHotkeys.Select(key => key.Action));
        Assert.False(model.HasPendingChanges);
    }

    [Fact]
    public void Keycaps_ShowConfiguredModifiersWithoutChangingTheGesture()
    {
        using var model = Create();
        var increase = model.ShortcutRows.Single(key => key.Action == HotkeyAction.IncreaseBlurStrength);
        Assert.Equal(new[] { "Ctrl", "Alt", "Shift", "↑" }, increase.GestureKeys.Select(key => key.Text));
        var hold = model.ShortcutRows.Single(key => key.Action == HotkeyAction.TemporaryRevealHold);
        Assert.Equal("Alt", Assert.Single(hold.GestureKeys).Text);
        Assert.Equal("(0.5 s)", hold.HoldDelayText);
        Assert.True(increase.ToModel().Modifiers.HasFlag(HotkeyModifiers.Alt));
    }

    [Fact]
    public void AttentionRow_ReflectsConflictOrPendingChangeInsteadOfFakingFailure()
    {
        var binding = new DefaultSettingsFactory().Create().GlobalHotkeys.First();
        var model = new HotkeyBindingViewModel(binding, true);
        Assert.True(model.HasAttentionMessage);
        Assert.Equal("Shortcut unavailable.", model.AttentionHeading);
        model.Enabled = false;
        Assert.False(model.ShowRegistrationFailure);
        Assert.Equal("Pending change.", model.AttentionHeading);
        var disabled = new HotkeyBindingViewModel(model.ToModel(), false);
        Assert.False(disabled.HasAttentionMessage);
        Assert.Equal("Disabled", disabled.RegistrationStatus);
    }

    [Fact]
    public void PanicExplanationTabs_DoNotPersistSettingsOrToggleProtection()
    {
        using var model = Create();
        var before = JsonSerializer.Serialize(model.ToModel());
        model.PanicHelpScope = PanicHelpScope.AllApps;
        Assert.Contains("Overview or the tray", model.PanicScopeSummary);
        Assert.Equal(before, JsonSerializer.Serialize(model.ToModel()));
        Assert.False(model.HasPendingChanges);
        Assert.Equal("Protection active", model.ProtectionBannerText);
    }

    [Fact]
    public void PinStatusAndAboutVersion_AreTakenFromActualData()
    {
        using var model = Create();
        Assert.Equal("No PIN configured", model.PanicPinCompactStatus);
        model.SetPanicPin(PanicPinService.Create("1234"));
        Assert.Equal("PIN configured", model.PanicPinCompactStatus);
        Assert.Equal("Change PIN", model.PanicPinActionLabel);
        Assert.True(model.HasPendingChanges);
        Assert.Equal($"PrivacyMask {AboutInformation.ProductVersion}", AboutInformation.ProductLabel);
        Assert.Equal($"Version {AboutInformation.ProductVersion}", AboutInformation.VersionText);
    }
}
