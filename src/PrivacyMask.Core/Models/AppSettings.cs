using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class AppSettings
{
    public const int CurrentVersion = 8;

    public int Version { get; init; } = CurrentVersion;

    public bool OnboardingCompleted { get; set; }

    public bool LaunchAtLogin { get; set; }

    public bool StartMinimized { get; set; } = true;

    // Retained in the serialized schema for backward compatibility. Runtime
    // protection state is transient and is normalized to Standard on load.
    public RuntimeMode CurrentMode { get; set; } = RuntimeMode.Standard;

    public List<HotkeyBinding> GlobalHotkeys { get; set; } = [];

    public List<AppProfile> AppProfiles { get; set; } = [];
}
