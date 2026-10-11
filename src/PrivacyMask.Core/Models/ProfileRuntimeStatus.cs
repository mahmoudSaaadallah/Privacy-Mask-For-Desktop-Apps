namespace PrivacyMask.Core.Models;

public enum ProfileRuntimeStatus
{
    Checking,
    Disabled,
    Protected,
    PanicProtected,
    ProtectionPaused,
    WaitingForFocus,
    NotRunning,
    TargetMissing,
}
