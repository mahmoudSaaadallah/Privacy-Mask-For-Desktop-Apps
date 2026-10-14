namespace PrivacyMask.App.Services;

public static class ProductVersionFormatter
{
    public static string Display(string? informationalVersion, Version? assemblyVersion)
    {
        var productVersion = informationalVersion?.Split('+')[0].Trim();
        return string.IsNullOrWhiteSpace(productVersion)
            ? assemblyVersion?.ToString(3) ?? "unknown"
            : productVersion;
    }
}
