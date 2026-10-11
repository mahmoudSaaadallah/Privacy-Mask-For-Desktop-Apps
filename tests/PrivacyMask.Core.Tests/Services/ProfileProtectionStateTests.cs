using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ProfileProtectionStateTests
{
    [Fact]
    public void Toggle_PausesAndResumesOnlyRequestedProfile()
    {
        var state = new ProfileProtectionState();

        Assert.True(state.Toggle("custom-notes"));
        Assert.True(state.IsPaused("CUSTOM-NOTES"));
        Assert.False(state.IsPaused("telegram"));

        Assert.False(state.Toggle("custom-notes"));
        Assert.False(state.IsPaused("custom-notes"));
    }

    [Fact]
    public void Prune_RemovesProfilesThatNoLongerExist()
    {
        var state = new ProfileProtectionState();
        state.Toggle("keep");
        state.Toggle("remove");

        state.Prune(["KEEP"]);

        Assert.True(state.IsPaused("keep"));
        Assert.False(state.IsPaused("remove"));
    }
}
