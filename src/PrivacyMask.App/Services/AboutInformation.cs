namespace PrivacyMask.App.Services;

public static class AboutInformation
{
    public const string Developer = "Mahmoud Saadallah";
    public const string SupportEmail = "mahmoud.saadallah73@gmail.com";
    public static Uri EmailUri { get; } = new($"mailto:{SupportEmail}");
    public static Uri GitHubUri { get; } = new("https://github.com/mahmoudsaaadallah");
    public static Uri LinkedInUri { get; } = new("https://linkedin.com/in/mahmoudsaaadallah");
    public static string ProductVersion => typeof(AboutInformation).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    public static string VersionText => $"Version {ProductVersion}";
    public static string ProductLabel => $"PrivacyMask {ProductVersion}";
    public static string GitHubDisplay => GitHubUri.Host + GitHubUri.AbsolutePath.TrimEnd('/');
    public static string LinkedInDisplay => LinkedInUri.Host + LinkedInUri.AbsolutePath.TrimEnd('/');
}
