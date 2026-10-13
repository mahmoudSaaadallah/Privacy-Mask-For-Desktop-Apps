using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.App.ViewModels;

public sealed class AppProfileViewModel : ObservableObject
{
    private string _displayName;
    private string? _executablePath;
    private ImageSource? _icon;
    private bool _enabled;
    private AppActivationMode _startupMode;
    private string _selectedPresetId;
    private double _maskIntensity;
    private MaskColorOption _maskColor;
    private int _hoverRevealWidthPixels;
    private int _hoverRevealHeightPixels;
    private ProfileRuntimeStatus _runtimeStatus = ProfileRuntimeStatus.Checking;
    private ObservableCollection<PrivacyZoneViewModel> _zones;

    public AppProfileViewModel(AppProfile profile)
    {
        ProfileId = profile.ProfileId;
        AppId = profile.AppId;
        _displayName = profile.DisplayName;
        _executablePath = profile.ExecutablePath;
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
                ProcessNameMatchMode = matcher.ProcessNameMatchMode,
                TitleContains = matcher.TitleContains,
                ClassNameContains = matcher.ClassNameContains,
            })
            .ToList();
    }

    public string ProfileId { get; }

    public AppId AppId { get; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public bool IsCustom => AppId == AppId.Custom;

    public bool IsBuiltIn => !IsCustom;

    public string? ExecutablePath => _executablePath;
    public ImageSource Icon => _icon ??= ApplicationIconCache.Get(ExecutablePath);

    public ProfileRuntimeStatus RuntimeStatus
    {
        get => _runtimeStatus;
        private set
        {
            if (SetProperty(ref _runtimeStatus, value))
            {
                RaisePropertyChanged(nameof(RuntimeStatusSummary));
                RaisePropertyChanged(nameof(RuntimeStatusColor));
            }
        }
    }

    public string RuntimeStatusSummary => RuntimeStatus switch
    {
        ProfileRuntimeStatus.Disabled => "Disabled",
        ProfileRuntimeStatus.Protected => "Protected now",
        ProfileRuntimeStatus.PanicProtected => "Protected now — panic mask active",
        ProfileRuntimeStatus.ProtectionPaused => "Running — protection paused",
        ProfileRuntimeStatus.WaitingForFocus => "Running — waiting for focus",
        ProfileRuntimeStatus.NotRunning => "Not running",
        ProfileRuntimeStatus.TargetMissing => "Not detected — relink may be required",
        _ => "Checking application status…",
    };

    public string RuntimeStatusColor => RuntimeStatus switch
    {
        ProfileRuntimeStatus.Protected or ProfileRuntimeStatus.PanicProtected => "#1F6E6C",
        ProfileRuntimeStatus.TargetMissing => "#A33A2B",
        ProfileRuntimeStatus.ProtectionPaused or ProfileRuntimeStatus.WaitingForFocus => "#8A6A38",
        _ => "#51656E",
    };

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
        set
        {
            if (SetProperty(ref _enabled, value) && !value)
            {
                RuntimeStatus = ProfileRuntimeStatus.Disabled;
            }
        }
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

    public void Relink(string processName, string? executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        WindowMatchers.Clear();
        WindowMatchers.Add(new WindowMatcher
        {
            ProcessNames = [processName.Trim()],
            ProcessNameMatchMode = ProcessNameMatchMode.Exact,
        });
        _executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : executablePath.Trim();
        RuntimeStatus = ProfileRuntimeStatus.Checking;
        _icon = null;
        RaisePropertyChanged(nameof(Icon));
        RaisePropertyChanged(nameof(ExecutablePath));
    }

    public void UpdateRuntimeStatus(ProfileRuntimeStatus status)
    {
        RuntimeStatus = status;
    }

    public AppProfile ToModel()
    {
        return new AppProfile
        {
            ProfileId = ProfileId,
            AppId = AppId,
            DisplayName = DisplayName,
            ExecutablePath = ExecutablePath,
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
                    ProcessNameMatchMode = matcher.ProcessNameMatchMode,
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
