using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class AppSettings
{
    public const int CurrentVersion = 5;

    public int Version { get; init; } = CurrentVersion;

    public bool OnboardingCompleted { get; set; }

    public bool LaunchAtLogin { get; set; }

    public bool StartMinimized { get; set; } = true;

    public RuntimeMode CurrentMode { get; set; } = RuntimeMode.Standard;

    public List<HotkeyBinding> GlobalHotkeys { get; set; } = [];

    public List<AppProfile> AppProfiles { get; set; } = [];
}
