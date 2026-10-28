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

    internal UpdateCheckResult? Preflight(UpdateReleaseChannel channel)
    {
        if (!Enum.IsDefined(channel)) return new(UpdateCheckStatus.InvalidContext);
        // Before even inspecting the installation or creating an HTTP client.
        if (!_verifier.IsConfigured) return new(UpdateCheckStatus.NotConfigured, Reason: RuntimeUpdatePreflight.DisabledBuild);
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ManifestRules.AppId);
            return RuntimeUpdatePreflight.Evaluate(_verifier.IsConfigured, OperatingSystem.IsWindows(),
                Environment.OSVersion.Version.Build, RuntimeInformation.ProcessArchitecture,
                Environment.ProcessPath, _settingsPath, root, ManagedStartupPath.Resolve);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or ReleaseValidationException)
        {
            return new(UpdateCheckStatus.ManualUpdateRequired, Reason: RuntimeUpdatePreflight.InstallationUnverified);
        }
    }

    internal Task<UpdateCheckResult> CheckAsync(UpdateReleaseChannel channel, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Preflight(channel) is { } unavailable) return Task.FromResult(unavailable);
        _client ??= new GitHubReleaseClient(_verifier);
        return _client.CheckAsync(Context(channel), token);
    }

    internal static UpdateCheckContext Context(UpdateReleaseChannel channel) => new(AboutInformation.FullVersion,
            channel == UpdateReleaseChannel.Preview ? UpdateChannel.Preview : UpdateChannel.Stable,
            "win-x64", Environment.OSVersion.Version.Build, 1, AppSettings.CurrentVersion, InstallationKind.Managed);

    public void Dispose() => _client?.Dispose();
}
