using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class CustomAppProfileFactory
{
    public static AppProfile Create(string displayName, string processName, string? profileId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        var presets = PresetCatalog.ClonePresets(PresetCatalog.GetDefaultPresets(AppId.Custom)).ToList();
        var selectedPreset = presets.Single();

        return new AppProfile
        {
            ProfileId = string.IsNullOrWhiteSpace(profileId)
                ? $"custom-{Guid.NewGuid():N}"
                : profileId.Trim(),
            AppId = AppId.Custom,
            DisplayName = displayName.Trim(),
            Enabled = true,
            StartupMode = AppActivationMode.Manual,
            MaskIntensity = MaskIntensityScale.Default,
            MaskColor = MaskColorOption.Black,
            HoverRevealWidthPixels = 394,
            HoverRevealHeightPixels = 42,
            WindowMatchers =
            [
                new WindowMatcher
                {
                    ProcessNames = [processName.Trim()],
                    ProcessNameMatchMode = ProcessNameMatchMode.Exact,
                },
            ],
            Presets = presets,
            SelectedPresetId = selectedPreset.PresetId,
            Zones = selectedPreset.Zones.Select(PresetCatalog.CloneZone).ToList(),
        };
    }
}
