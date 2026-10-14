using System.Reflection;

namespace PrivacyMask.App.Services;

public static class AboutInformation
{
    public const string Developer = "Mahmoud Saadallah";
    public const string SupportEmail = "mahmoud.saadallah73@gmail.com";
    public static Uri EmailUri { get; } = new($"mailto:{SupportEmail}");
    public static Uri GitHubUri { get; } = new("https://github.com/mahmoudsaaadallah");
    public static Uri LinkedInUri { get; } = new("https://linkedin.com/in/mahmoudsaaadallah");
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
