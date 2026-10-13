using PrivacyMask.Core.Models;

namespace PrivacyMask.App.ViewModels;

public sealed record ActivationModeOption(AppActivationMode Value, string DisplayName)
{
    public static IReadOnlyList<ActivationModeOption> All { get; } =
    [
        new(AppActivationMode.Manual, "Always while visible"),
        new(AppActivationMode.FocusAware, "Only while focused"),
    ];
}
