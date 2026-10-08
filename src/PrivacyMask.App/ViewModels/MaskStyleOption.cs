using PrivacyMask.Core.Models;

namespace PrivacyMask.App.ViewModels;

public sealed record MaskStyleOption(MaskStyle Value, string DisplayName)
{
    public static IReadOnlyList<MaskStyleOption> All { get; } =
    [
        new(MaskStyle.FrostedGlass, "Frosted glass"),
        new(MaskStyle.Pixelate, "Pixelated"),
        new(MaskStyle.SolidRedact, "Solid redact"),
    ];

    public static string GetDisplayName(MaskStyle style)
    {
        return All.FirstOrDefault(option => option.Value == style)?.DisplayName
            ?? "Frosted glass";
    }
}
