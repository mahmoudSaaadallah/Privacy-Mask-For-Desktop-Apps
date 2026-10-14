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

    public static bool IsNextListPage(Uri target, int currentPage)
    {
        if (currentPage is < 1 or >= 5 || !target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps ||
            target.Host != "api.github.com" || !target.IsDefaultPort || target.UserInfo.Length != 0 || target.Fragment.Length != 0)
            return false;
        // GitHub may return numeric repository aliases in Link. Never follow
        // that target: the caller always constructs our fixed named-repo page.
        if (!string.Equals(target.AbsolutePath, ListPage(currentPage + 1).AbsolutePath, StringComparison.OrdinalIgnoreCase) &&
            !System.Text.RegularExpressions.Regex.IsMatch(target.AbsolutePath, @"\A/repositories/[0-9]{1,20}/releases\z")) return false;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in target.Query.TrimStart('?').Split('&'))
        {
            var pair = item.Split('=', 2);
            if (pair.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]))) return false;
        }
        return query.Count == 2 && query.GetValueOrDefault("per_page") == "30" &&
            query.GetValueOrDefault("page") == (currentPage + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

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
