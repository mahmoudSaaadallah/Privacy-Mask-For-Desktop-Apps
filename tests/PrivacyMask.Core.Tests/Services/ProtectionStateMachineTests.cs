using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ProtectionStateMachineTests
{
    [Fact]
    public void NewStateMachine_StartsWithProtectionEnabled()
    {
        var stateMachine = new ProtectionStateMachine();

        Assert.Equal(RuntimeMode.Standard, stateMachine.CurrentMode);
        Assert.True(stateMachine.IsTemporaryRevealAllowed);
    }

    [Fact]
    public void ToggleProtection_PausesAndResumesProtection()
    {
        var stateMachine = new ProtectionStateMachine();

        Assert.Equal(RuntimeMode.Off, stateMachine.ToggleProtection());
        Assert.False(stateMachine.IsTemporaryRevealAllowed);
        Assert.Equal(RuntimeMode.Standard, stateMachine.ToggleProtection());
        Assert.True(stateMachine.IsTemporaryRevealAllowed);
    }

    [Fact]
    public void TogglePanic_AlwaysReturnsToProtectedMode()
    {
        var stateMachine = new ProtectionStateMachine();
        stateMachine.ToggleProtection();

        Assert.Equal(RuntimeMode.Panic, stateMachine.TogglePanic());
        Assert.False(stateMachine.IsTemporaryRevealAllowed);
        Assert.Equal(RuntimeMode.Standard, stateMachine.TogglePanic());
        Assert.True(stateMachine.IsTemporaryRevealAllowed);
    }

    [Fact]
    public void ToggleProtection_DoesNotBypassPanicMask()
    {
        var stateMachine = new ProtectionStateMachine();
        stateMachine.TogglePanic();

        Assert.Equal(RuntimeMode.Panic, stateMachine.ToggleProtection());
        Assert.Equal(RuntimeMode.Panic, stateMachine.CurrentMode);
    }

    [Fact]
    public void PauseCannotBypassAnIndividualPanicMask()
    {
        var stateMachine = new ProtectionStateMachine();
        var profiles = new ProfileProtectionState();
        profiles.TogglePanic("notes");

        Assert.Equal(RuntimeMode.Standard, stateMachine.ToggleProtection(profiles.HasAnyPanic));
        Assert.Equal(RuntimeMode.Panic, profiles.ResolveMode("notes", stateMachine.CurrentMode));
        Assert.True(ProtectionRenderPolicy.ForMode(profiles.ResolveMode("notes", stateMachine.CurrentMode)).ForceFullWindowMask);
    }

    [Fact]
    public void EnsureProtected_PreservesPanic()
    {
        var stateMachine = new ProtectionStateMachine();
        stateMachine.TogglePanic();

        Assert.Equal(RuntimeMode.Panic, stateMachine.EnsureProtected());
        Assert.Equal(RuntimeMode.Panic, stateMachine.CurrentMode);
    }
}
