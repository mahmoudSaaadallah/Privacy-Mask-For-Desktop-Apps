using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.App.ViewModels;

public sealed class AppProfileViewModel : ObservableObject
{
    private bool _enabled;
    private AppActivationMode _startupMode;
    private string _selectedPresetId;
    private double _maskIntensity;
    private MaskColorOption _maskColor;
    private int _hoverRevealWidthPixels;
    private int _hoverRevealHeightPixels;
    private ObservableCollection<PrivacyZoneViewModel> _zones;

    public AppProfileViewModel(AppProfile profile)
    {
        ProfileId = profile.ProfileId;
        AppId = profile.AppId;
        DisplayName = profile.DisplayName;
        _enabled = profile.Enabled;
        _startupMode = profile.StartupMode;
        _selectedPresetId = profile.SelectedPresetId;
        _maskIntensity = profile.MaskIntensity;
        _maskColor = profile.MaskColor;
        _hoverRevealWidthPixels = profile.HoverRevealWidthPixels;
        _hoverRevealHeightPixels = profile.HoverRevealHeightPixels;
        Presets = new ObservableCollection<LayoutPreset>(profile.Presets.Select(PresetCatalog.ClonePreset));
        _zones = new ObservableCollection<PrivacyZoneViewModel>(profile.Zones.Select(zone => new PrivacyZoneViewModel(zone)));
        WindowMatchers = profile.WindowMatchers
            .Select(matcher => new WindowMatcher
            {
                ProcessNames = [.. matcher.ProcessNames],
                TitleContains = matcher.TitleContains,
                ClassNameContains = matcher.ClassNameContains,
            })
            .ToList();
    }

    public string ProfileId { get; }

    public AppId AppId { get; }

    public string DisplayName { get; }

    public string Summary => StartupMode == AppActivationMode.FocusAware
        ? "Masks only while the supported app is focused."
        : "Covers the whole app window even when it is visible but inactive.";

    public string MaskIntensitySummary => $"Blur strength: {MaskIntensityPercent:0}%";

    public string MaskStyleSummary => $"Mask style: {MaskStyleOption.GetDisplayName(PrimaryMaskStyle)}";

    public string MaskColorSummary => $"Mask color: {MaskColor}";

    public string HoverRevealSummary => $"Hover reveal window: {HoverRevealWidthPixels} x {HoverRevealHeightPixels} px";

    public ObservableCollection<LayoutPreset> Presets { get; }

    public List<WindowMatcher> WindowMatchers { get; }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public AppActivationMode StartupMode
    {
        get => _startupMode;
        set
        {
            if (SetProperty(ref _startupMode, value))
            {
                RaisePropertyChanged(nameof(Summary));
            }
        }
    }

    public string SelectedPresetId
    {
        get => _selectedPresetId;
        set => SetProperty(ref _selectedPresetId, value);
    }

    public double MaskIntensity
    {
        get => _maskIntensity;
        set
        {
            var normalized = MaskIntensityScale.Clamp(value);
            if (SetProperty(ref _maskIntensity, normalized))
            {
                RaisePropertyChanged(nameof(MaskIntensityPercent));
                RaisePropertyChanged(nameof(MaskIntensitySummary));
            }
        }
    }

    public double MaskIntensityPercent
    {
        get => MaskIntensity * 100d;
        set
        {
            MaskIntensity = value / 100d;
            RaisePropertyChanged(nameof(MaskIntensityPercent));
        }
    }

    public MaskColorOption MaskColor
    {
        get => _maskColor;
        set
        {
            if (SetProperty(ref _maskColor, value))
            {
                RaisePropertyChanged(nameof(MaskColorSummary));
            }
        }
    }

    public MaskStyle PrimaryMaskStyle
    {
        get => Zones.FirstOrDefault()?.Style ?? MaskStyle.Blur;
        set
        {
            var changed = false;
            foreach (var zone in Zones)
            {
                if (zone.Style == value)
                {
                    continue;
                }

                zone.Style = value;
                changed = true;
            }

            if (changed)
            {
                RaisePropertyChanged(nameof(PrimaryMaskStyle));
                RaisePropertyChanged(nameof(MaskStyleSummary));
            }
        }
    }

    public ObservableCollection<PrivacyZoneViewModel> Zones
    {
        get => _zones;
        private set => SetProperty(ref _zones, value);
    }

    public int HoverRevealWidthPixels
    {
        get => _hoverRevealWidthPixels;
        set
        {
            if (SetProperty(ref _hoverRevealWidthPixels, value))
            {
                RaisePropertyChanged(nameof(HoverRevealSummary));
            }
        }
    }

    public int HoverRevealHeightPixels
    {
        get => _hoverRevealHeightPixels;
        set
        {
            if (SetProperty(ref _hoverRevealHeightPixels, value))
            {
                RaisePropertyChanged(nameof(HoverRevealSummary));
            }
        }
    }

    public void ApplySelectedPreset()
    {
        var selectedPreset = Presets.FirstOrDefault(preset => preset.PresetId == SelectedPresetId)
            ?? Presets.FirstOrDefault();
        if (selectedPreset is null)
        {
            return;
        }

        Zones = new ObservableCollection<PrivacyZoneViewModel>(
            selectedPreset.Zones.Select(zone => new PrivacyZoneViewModel(PresetCatalog.CloneZone(zone))));
        RaisePropertyChanged(nameof(PrimaryMaskStyle));
        RaisePropertyChanged(nameof(MaskStyleSummary));
    }

    public AppProfile ToModel()
    {
        return new AppProfile
        {
            ProfileId = ProfileId,
            AppId = AppId,
            DisplayName = DisplayName,
            Enabled = Enabled,
            StartupMode = StartupMode,
            MaskIntensity = MaskIntensity,
            MaskColor = MaskColor,
            HoverRevealWidthPixels = HoverRevealWidthPixels,
            HoverRevealHeightPixels = HoverRevealHeightPixels,
            WindowMatchers = WindowMatchers
                .Select(matcher => new WindowMatcher
                {
                    ProcessNames = [.. matcher.ProcessNames],
                    TitleContains = matcher.TitleContains,
                    ClassNameContains = matcher.ClassNameContains,
                })
                .ToList(),
            Presets = Presets.Select(PresetCatalog.ClonePreset).ToList(),
            SelectedPresetId = SelectedPresetId,
            Zones = Zones.Select(zone => zone.ToModel()).ToList(),
        };
    }
}
