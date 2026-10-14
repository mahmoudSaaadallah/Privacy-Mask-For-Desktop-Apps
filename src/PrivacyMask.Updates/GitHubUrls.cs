namespace PrivacyMask.Updates;

public static class GitHubUrls
{
    public static Uri ReleasesPage { get; } = new($"https://github.com/{ManifestRules.Repository}/releases");
    public static Uri ReleasePage(string tag) => new($"{ReleasesPage}/tag/{Uri.EscapeDataString(tag)}");
    public static Uri ListPage(int page) => page is >= 1 and <= 5
        ? new($"https://api.github.com/repos/{ManifestRules.Repository}/releases?per_page=30&page={page}")
        : throw new ArgumentOutOfRangeException(nameof(page));
    public static Uri AssetApi(long id) => id > 0
        ? new($"https://api.github.com/repos/{ManifestRules.Repository}/releases/assets/{id}")
        : throw new ArgumentOutOfRangeException(nameof(id));
    public static Uri BrowserAsset(string tag, string name) =>
        new($"{ReleasesPage}/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}");

    public static bool IsAllowedAssetTarget(Uri target, Uri assetApi, Uri browserAsset)
    {
        if (!target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps || !target.IsDefaultPort ||
            target.UserInfo.Length != 0 || target.Fragment.Length != 0) return false;
        if (target == assetApi || target == browserAsset) return true;
        return (target.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com") &&
            (target.AbsolutePath.StartsWith("/github-production-release-asset/", StringComparison.Ordinal) ||
             target.AbsolutePath.StartsWith("/github-production-release-asset-", StringComparison.Ordinal));
    }
}
