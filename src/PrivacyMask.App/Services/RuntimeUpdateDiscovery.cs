using System.IO;
using System.Runtime.InteropServices;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal sealed class RuntimeUpdateDiscovery : IDisposable
{
    private readonly string _settingsPath;
    private readonly UpdateManifestVerifier _verifier = UpdateRuntimePolicy.CreateVerifier();
    private GitHubReleaseClient? _client;

    internal RuntimeUpdateDiscovery(string settingsPath) => _settingsPath = settingsPath;

    internal Task<UpdateCheckResult> CheckAsync(UpdateReleaseChannel channel, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Before even inspecting the installation or creating an HTTP client.
        if (!_verifier.IsConfigured) return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.NotConfigured));
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ManifestRules.AppId);
            var app = Environment.ProcessPath;
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || app is null ||
                !SamePath(app, Path.Combine(root, "app", LauncherBoot.ApplicationFileName)) ||
                !SamePath(_settingsPath, Path.Combine(root, "settings.v1.json")) || ManagedStartupPath.Resolve(app) is null)
                return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.ManualUpdateRequired));
            _client ??= new GitHubReleaseClient(_verifier);
            return _client.CheckAsync(new(AboutInformation.FullVersion,
                channel == UpdateReleaseChannel.Preview ? UpdateChannel.Preview : UpdateChannel.Stable,
                "win-x64", Environment.OSVersion.Version.Build, 1, AppSettings.CurrentVersion, InstallationKind.Managed), token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.ManualUpdateRequired));
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _client?.Dispose();
}
