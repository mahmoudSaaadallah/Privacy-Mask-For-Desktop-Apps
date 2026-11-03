using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public sealed class ProtectionStateMachine
{
    public RuntimeMode CurrentMode { get; private set; } = RuntimeMode.Standard;

    public bool IsTemporaryRevealAllowed => CurrentMode == RuntimeMode.Standard;

    public RuntimeMode ToggleProtection(bool hasProfilePanic = false)
    {
        if (CurrentMode == RuntimeMode.Panic || hasProfilePanic)
        {
            return CurrentMode;
        }

        CurrentMode = CurrentMode == RuntimeMode.Off
            ? RuntimeMode.Standard
            : RuntimeMode.Off;

        return CurrentMode;
    }

    public RuntimeMode TogglePanic()
    {
        CurrentMode = CurrentMode == RuntimeMode.Panic
            ? RuntimeMode.Standard
            : RuntimeMode.Panic;

        return CurrentMode;
    }

    public RuntimeMode EnsureProtected()
    {
        if (CurrentMode == RuntimeMode.Panic) return CurrentMode;
        CurrentMode = RuntimeMode.Standard;
        return CurrentMode;
    }
}
