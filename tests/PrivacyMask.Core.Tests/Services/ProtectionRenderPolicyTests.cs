using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ProtectionRenderPolicyTests
{
    [Fact]
    public void StandardMode_UsesConfiguredZonesAndAllowsHoverReveal()
    {
        var policy = ProtectionRenderPolicy.ForMode(RuntimeMode.Standard);

        Assert.True(policy.ShouldRender);
        Assert.False(policy.ForceFullWindowMask);
        Assert.True(policy.AllowHoverReveal);
    }

    [Fact]
    public void OffMode_DoesNotRenderProtection()
    {
        var policy = ProtectionRenderPolicy.ForMode(RuntimeMode.Off);

        Assert.False(policy.ShouldRender);
        Assert.False(policy.AllowHoverReveal);
    }

    [Fact]
    public void PanicMode_ForcesOpaqueBlackFullWindowMaskWithoutReveal()
    {
        var policy = ProtectionRenderPolicy.ForMode(RuntimeMode.Panic);

        Assert.True(policy.ShouldRender);
        Assert.True(policy.ForceFullWindowMask);
        Assert.False(policy.AllowHoverReveal);
        Assert.Equal(MaskStyle.SolidRedact, policy.ForcedStyle);
        Assert.Equal(MaskColorOption.Black, policy.ForcedColor);
        Assert.Equal(MaskIntensityScale.Maximum, policy.ForcedStrength);
    }
}
