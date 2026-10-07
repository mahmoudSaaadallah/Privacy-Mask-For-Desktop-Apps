using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class OverlayRefreshCadenceTests
{
    [Theory]
    [InlineData(RuntimeMode.Standard)]
    [InlineData(RuntimeMode.Panic)]
    [InlineData(RuntimeMode.TemporaryReveal)]
    public void Select_UsesActiveCadenceWhenCandidateWindowsExist(RuntimeMode mode)
    {
        Assert.Equal(OverlayRefreshCadence.ActiveInterval, OverlayRefreshCadence.Select(mode, hasCandidateWindows: true));
    }

    [Fact]
    public void Select_UsesIdleCadenceWhenNoCandidateWindowsExist()
    {
        Assert.Equal(
            OverlayRefreshCadence.IdleInterval,
            OverlayRefreshCadence.Select(RuntimeMode.Standard, hasCandidateWindows: false));
    }

    [Fact]
    public void Select_UsesIdleCadenceWhileProtectionIsPaused()
    {
        Assert.Equal(
            OverlayRefreshCadence.IdleInterval,
            OverlayRefreshCadence.Select(RuntimeMode.Off, hasCandidateWindows: true));
    }
}
