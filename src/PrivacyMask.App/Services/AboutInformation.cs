using System.Reflection;

namespace PrivacyMask.App.Services;

public static class AboutInformation
{
    public const string Developer = "Mahmoud Saadallah";
    public const string SupportEmail = "mahmoud.saadallah73@gmail.com";
    public const string UpdatePrivacyDisclosure = "Checks use saved preferences. No network or startup timer is created without approved signing keys and a supported managed installation. Once configured, one check may run 30 seconds after startup if the last attempt is at least 24 hours old. Save opt-out to cancel pending automatic work; enabling takes effect on the next startup. Manual checks and explicit downloads respect GitHub retry deadlines. Download streams and verifies a separate package while protection stays active; Cancel stops incomplete work and Discard removes only this session's verified download. Nothing is installed or restarted in this build. GitHub receives ordinary connection metadata, including your IP address, never profiles, PINs, paths, window titles or captures. View releases opens your browser.";
    public static Uri EmailUri { get; } = new($"mailto:{SupportEmail}");
    public static Uri GitHubUri { get; } = new("https://github.com/mahmoudsaaadallah");
    public static Uri LinkedInUri { get; } = new("https://linkedin.com/in/mahmoudsaaadallah");
    public static Uri ReleasesUri { get; } = new("https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases");
    private static readonly Assembly ProductAssembly = typeof(AboutInformation).Assembly;
    public static string FullVersion { get; } = ProductAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? ProductAssembly.GetName().Version?.ToString(3) ?? "unknown";
    public static string WindowsFileVersion { get; } = ProductAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
    public static string ProductVersion => ProductVersionFormatter.Display(FullVersion, ProductAssembly.GetName().Version);
    public static string BuildDescription => $"Product: {FullVersion}\nWindows file version: {WindowsFileVersion}";
    public static string VersionText => $"Version {ProductVersion}";
    public static string ProductLabel => $"PrivacyMask {ProductVersion}";
    public static string GitHubDisplay => GitHubUri.Host + GitHubUri.AbsolutePath.TrimEnd('/');
    public static string LinkedInDisplay => LinkedInUri.Host + LinkedInUri.AbsolutePath.TrimEnd('/');
}
