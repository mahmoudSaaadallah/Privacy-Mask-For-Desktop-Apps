namespace PrivacyMask.Core.Models;

public sealed class HotkeyBinding
{
    public HotkeyAction Action { get; init; }

    public HotkeyModifiers Modifiers { get; init; }

    public int VirtualKey { get; init; }

    public bool Enabled { get; set; } = true;

    public bool IsHoldGesture { get; init; }

    public string DisplayName { get; init; } = string.Empty;
}
