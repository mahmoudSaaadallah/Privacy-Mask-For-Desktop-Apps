using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class LauncherApplyTests
{
    [Fact]
    public async Task Activated_preview_still_refuses_an_unrelated_launcher_without_network_or_pipe_activity()
    {
        using var fixture = new UpdateDiscoveryFixture();
        var verifier = UpdateRuntimePolicy.CreateVerifier();
        using var discovery = new GitHubReleaseClient(verifier, fixture.Clock, fixture.Handler);
        var request = new UpdateApplyRequest(UpdateHandoffServer.PipePrefix + Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), 123, 456, UpdateStartMode.Normal);
        Assert.True(verifier.IsConfigured);
        await Assert.ThrowsAsync<ReleaseValidationException>(() => LauncherApply.RunAsync(
            @"C:\Unrelated\PrivacyMask.Launcher.exe", request.ToArguments(), verifier, discovery));
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("--UPDATE-APPLY")]
    [InlineData("--update-apply")]
    [InlineData("--minimized")]
    public async Task Malformed_apply_cannot_fall_back_to_normal_startup(string argument)
    {
        using var fixture = new UpdateDiscoveryFixture();
        using var discovery = fixture.Client();
        await Assert.ThrowsAsync<ReleaseValidationException>(() => LauncherApply.RunAsync(
            @"C:\Unrelated\PrivacyMask.Launcher.exe", [argument], fixture.Verifier, discovery));
        Assert.Empty(fixture.Handler.Requests);
    }
}
