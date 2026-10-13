using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ShortcutPresentationTests
{
    [Fact]
    public void EnableChange_IsPendingUntilSavedAndRestoresRegistrationFeedback()
    {
        var binding = new DefaultSettingsFactory().Create().GlobalHotkeys.First();
        var model = new HotkeyBindingViewModel(binding, hasRegistrationFailure: true);
        Assert.True(model.ShowRegistrationFailure);
        Assert.Contains("restart PrivacyMask", model.RegistrationStatus);
        model.Enabled = false;
        Assert.False(model.ShowRegistrationFailure);
        Assert.Contains("Pending", model.RegistrationStatus);
        model.Enabled = true;
        Assert.True(model.ShowRegistrationFailure);
        Assert.Equal(binding.Modifiers, model.ToModel().Modifiers);
        Assert.Equal(binding.VirtualKey, model.ToModel().VirtualKey);
    }

    [Fact]
    public void DisabledBinding_DoesNotClaimToBeRegistered()
    {
        var binding = new DefaultSettingsFactory().Create().GlobalHotkeys.First();
        binding.Enabled = false;
        var model = new HotkeyBindingViewModel(binding, false);
        Assert.Equal("Disabled", model.RegistrationStatus);
        model.Enabled = true;
        Assert.Contains("Pending", model.RegistrationStatus);
    }

    [Fact]
    public void FocusedActionsAndHoldDelay_AreExplainedWithoutChangingGestures()
    {
        var settings = new DefaultSettingsFactory().Create();
        foreach (var binding in settings.GlobalHotkeys.Where(binding => binding.Action != HotkeyAction.OpenSettings))
        {
            var model = new HotkeyBindingViewModel(binding, false);
            Assert.Contains("Focused", model.ScopeSummary);
            if (binding.Action == HotkeyAction.TemporaryRevealHold)
            {
                Assert.Contains("0.5 seconds", model.ScopeSummary);
                Assert.Contains("cancels", model.ScopeSummary);
                Assert.Equal("Hold gesture enabled", model.RegistrationStatus);
            }
        }
    }
}
