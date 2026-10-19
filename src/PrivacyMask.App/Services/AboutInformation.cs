using System.Reflection;

namespace PrivacyMask.App.Services;

public static class AboutInformation
{
    public const string Developer = "Mahmoud Saadallah";
    public const string SupportEmail = "mahmoud.saadallah73@gmail.com";
    public const string UpdatePrivacyDisclosure = "Required updates cannot be disabled or cancelled inside PrivacyMask. Once signed runtime activation and a managed installation are approved, checks run at startup, reconnect and every 24 hours after success. Failed requests retry no sooner than 15 minutes and respect persisted GitHub retry deadlines. Network availability is only a hint, not proof of Internet access. Newer compatible signed versions download automatically and install without confirmation after Panic is unlocked, edits are resolved and protected windows are closed or minimized. Protection stays active while waiting; unsafe or uncertain state delays restart. Windows can still stop or block this user-owned process. Activation-disabled builds create no automatic timer or network subscription. GitHub receives ordinary connection metadata, including your IP address, never profiles, PINs, paths, window titles or captures. View releases opens your browser.";
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
