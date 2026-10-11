using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class DesktopApplicationCatalogTests
{
    [Fact]
    public void TryCreateFromForegroundWindow_CreatesFallbackCandidate_WhenExecutableCannotBeInspected()
    {
        var catalog = new DesktopApplicationCatalog(new DesktopWindowInspector());
        var snapshot = CreateSnapshot("SampleEditor");

        var created = catalog.TryCreateFromForegroundWindow(
            snapshot,
            [],
            out var candidate,
            out var failureReason);

        Assert.True(created, failureReason);
        Assert.NotNull(candidate);
        Assert.Equal("SampleEditor", candidate!.ProcessName);
        Assert.Equal("SampleEditor", candidate.DisplayName);
        Assert.True(candidate.IsForeground);
        Assert.True(candidate.IsRunning);
    }

    [Theory]
    [InlineData("explorer", "Windows system surface")]
    [InlineData("AlreadyProtected", "already protected")]
    public void TryCreateFromForegroundWindow_RejectsUnsafeOrExistingProcesses(
        string processName,
        string expectedReason)
    {
        var catalog = new DesktopApplicationCatalog(new DesktopWindowInspector());
        var protectedProcesses = string.Equals(processName, "AlreadyProtected", StringComparison.Ordinal)
            ? new[] { processName }
            : [];

        var created = catalog.TryCreateFromForegroundWindow(
            CreateSnapshot(processName),
            protectedProcesses,
            out var candidate,
            out var failureReason);

        Assert.False(created);
        Assert.Null(candidate);
        Assert.Contains(expectedReason, failureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static WindowSnapshot CreateSnapshot(string processName)
    {
        return new WindowSnapshot
        {
            Handle = 123456,
            ProcessName = processName,
            Title = "Focused document",
            ClassName = "SampleWindow",
            Bounds = new ScreenRect(10, 10, 900, 700),
            ZOrderIndex = 0,
            IsVisible = true,
            IsForeground = true,
            IsMinimized = false,
        };
    }
}
