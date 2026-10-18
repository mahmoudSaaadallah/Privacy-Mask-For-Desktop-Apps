using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class AppSettings
{
    public const int CurrentVersion = 13;

    public int Version { get; init; } = CurrentVersion;

    public bool OnboardingCompleted { get; set; }

    public bool LaunchAtLogin { get; set; }

    public bool StartMinimized { get; set; } = true;

    public bool AutomaticUpdateChecks { get; set; } = true;

    public UpdateReleaseChannel UpdateChannel { get; set; } = UpdatePreferenceDefaults.Channel;

    // Retained in the serialized schema for backward compatibility. Runtime
    // protection state is transient and is normalized to Standard on load.
    public RuntimeMode CurrentMode { get; set; } = RuntimeMode.Standard;

    public PanicPinCredential? PanicPin { get; set; }

    public List<HotkeyBinding> GlobalHotkeys { get; set; } = [];

    public List<AppProfile> AppProfiles { get; set; } = [];
}
