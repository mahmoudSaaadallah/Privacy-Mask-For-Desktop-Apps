using System.IO;
using System.Runtime.InteropServices;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RuntimeUpdatePreflightTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "Synthetic", ManifestRules.AppId);
    private static readonly string App = Path.Combine(Root, "app", LauncherBoot.ApplicationFileName);
    private static readonly string Settings = Path.Combine(Root, "settings.v1.json");

    [Theory]
    [InlineData(false, true, 26100, Architecture.X64, RuntimeUpdatePreflight.DisabledBuild)]
    [InlineData(true, false, 26100, Architecture.X64, RuntimeUpdatePreflight.UnsupportedSystem)]
    [InlineData(true, true, 21999, Architecture.X64, RuntimeUpdatePreflight.UnsupportedSystem)]
    [InlineData(true, true, 26100, Architecture.Arm64, RuntimeUpdatePreflight.UnsupportedArchitecture)]
    public void UnsupportedFactsDoNotInspectAnyPath(bool configured, bool windows, int build, Architecture architecture, string reason)
    {
        var result = RuntimeUpdatePreflight.Evaluate(configured, windows, build, architecture,
            "\0invalid", "\0invalid", "\0invalid", _ => throw new Exception("No filesystem access allowed"));
        Assert.Equal(reason, result!.Reason);
    }

    [Fact]
    public void OutsideCopyDoesNotInspectSettingsOrPretendItIsManaged()
    {
        var result = RuntimeUpdatePreflight.Evaluate(true, true, 26100, Architecture.X64,
            Path.Combine(Root, "single-file", LauncherBoot.ApplicationFileName), "\0invalid", Root,
            _ => throw new Exception("Must not inspect installation"));
        Assert.Equal(RuntimeUpdatePreflight.OutsideManagedPath, result!.Reason);
        Assert.Equal(UpdateCheckStatus.ManualUpdateRequired, result.Status);
        var status = new UpdateStatusViewModel();
        status.Complete(result, UpdateReleaseChannel.Preview, DateTimeOffset.UtcNow);
        Assert.Contains("Exit it from the tray", status.StatusDetail);
        Assert.Contains("Desktop or Start Menu shortcut", status.StatusDetail);
    }

    [Fact]
    public void MatchingPathsStillRequireManagedInstallationVerification()
    {
        Assert.Equal(RuntimeUpdatePreflight.InstallationUnverified,
            Check(_ => null)!.Reason);
        Assert.Equal(RuntimeUpdatePreflight.InstallationUnverified,
            Check(_ => throw new UnauthorizedAccessException("private path"))!.Reason);
        Assert.Null(Check(_ => "verified launcher fixture"));
        Assert.Equal(RuntimeUpdatePreflight.InstallationUnverified,
            RuntimeUpdatePreflight.Evaluate(true, true, 26100, Architecture.X64, App, "\0invalid", Root,
                _ => throw new Exception("Invalid settings must be rejected first"))!.Reason);
    }

    private static UpdateCheckResult? Check(Func<string, string?> resolve) =>
        RuntimeUpdatePreflight.Evaluate(true, true, 26100, Architecture.X64, App, Settings, Root, resolve);
}
