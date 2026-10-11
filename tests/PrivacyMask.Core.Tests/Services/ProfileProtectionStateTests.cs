using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ProfileProtectionStateTests
{
    [Fact]
    public void Toggle_PausesAndResumesOnlyRequestedProfile()
    {
        var state = new ProfileProtectionState();

        Assert.True(state.TogglePause("custom-notes"));
        Assert.True(state.IsPaused("CUSTOM-NOTES"));
        Assert.False(state.IsPaused("telegram"));

        Assert.False(state.TogglePause("custom-notes"));
        Assert.False(state.IsPaused("custom-notes"));
    }

    [Fact]
    public void Prune_RemovesProfilesThatNoLongerExist()
    {
        var state = new ProfileProtectionState();
        state.TogglePause("keep");
        state.TogglePanic("keep");
        state.TogglePause("remove");
        state.TogglePanic("remove");

        state.Prune(["KEEP"]);

        Assert.True(state.IsPaused("keep"));
        Assert.True(state.IsPanic("keep"));
        Assert.False(state.IsPaused("remove"));
        Assert.False(state.IsPanic("remove"));
    }

    [Fact]
    public void TogglePanic_AffectsOnlyRequestedProfile()
    {
        var state = new ProfileProtectionState();

        Assert.True(state.TogglePanic("custom-notes"));
        Assert.True(state.IsPanic("CUSTOM-NOTES"));
        Assert.False(state.IsPanic("telegram"));
        Assert.Equal(RuntimeMode.Panic, state.ResolveMode("custom-notes", RuntimeMode.Standard));
        Assert.Equal(RuntimeMode.Standard, state.ResolveMode("telegram", RuntimeMode.Standard));

        Assert.False(state.TogglePanic("custom-notes"));
        Assert.False(state.IsPanic("custom-notes"));
    }

    [Fact]
    public void ResolveMode_GlobalPanicOverridesEveryProfile()
    {
        var state = new ProfileProtectionState();

        Assert.Equal(RuntimeMode.Panic, state.ResolveMode("whatsapp", RuntimeMode.Panic));
        Assert.Equal(RuntimeMode.Off, state.ResolveMode("whatsapp", RuntimeMode.Off));
    }
}
