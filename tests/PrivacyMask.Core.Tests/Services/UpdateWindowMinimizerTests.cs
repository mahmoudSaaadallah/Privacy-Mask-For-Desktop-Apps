using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateWindowMinimizerTests
{
    [Fact]
    public async Task SuccessfulRequestWaitsForAllWindowsToActuallyMinimize()
    {
        var windows = new WindowSnapshot[] { new() { Handle = 1 }, new() { Handle = 2 } };
        var minimized = new List<nint>();
        var waits = 0;
        Assert.True(await UpdateWindowMinimizer.RunAsync(() => windows,
            window => { minimized.Add(window.Handle); return true; }, () => true,
            () => { waits++; windows = []; return Task.CompletedTask; }));
        Assert.Equal(new nint[] { 1, 2 }, minimized);
        Assert.Equal(1, waits);
    }

    [Fact]
    public async Task NonresponsiveWindowDoesNotTurnRequestSuccessIntoPermissionToUnlock()
    {
        var requests = 0;
        var waits = 0;
        Assert.False(await UpdateWindowMinimizer.RunAsync(() => [new() { Handle = 1 }],
            _ => { requests++; return true; }, () => true,
            () => { waits++; return Task.CompletedTask; }));
        Assert.Equal(1, requests);
        Assert.Equal(20, waits);
    }

    [Fact]
    public async Task NewWindowIsAlsoMinimizedAndChangedContextStopsFurtherRequests()
    {
        var windows = new WindowSnapshot[] { new() { Handle = 1 } };
        var requests = new List<nint>();
        var allowed = true;
        Assert.False(await UpdateWindowMinimizer.RunAsync(() => windows,
            window => { requests.Add(window.Handle); if (window.Handle == 2) allowed = false; return true; },
            () => allowed, () =>
            {
                windows = [new() { Handle = 2 }, new() { Handle = 3 }];
                return Task.CompletedTask;
            }));
        Assert.Equal(new nint[] { 1, 2 }, requests);
    }

    [Fact]
    public async Task EnumerationFailureAndRejectedRequestCannotReportSuccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateWindowMinimizer.RunAsync(
            () => throw new InvalidOperationException("Incomplete enumeration"), _ => true, () => true));
        Assert.False(await UpdateWindowMinimizer.RunAsync(() => [new() { Handle = 1 }], _ => false, () => true));
    }
}
