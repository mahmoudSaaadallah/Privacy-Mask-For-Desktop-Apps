using System.Collections.Generic;
using System.Windows.Input;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.Services;

public static class HotkeyDisplayFormatter
{
    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        var key = binding.VirtualKey switch
        {
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            _ => KeyInterop.KeyFromVirtualKey(binding.VirtualKey).ToString(),
        };

        if (binding.IsHoldGesture)
        {
            return $"Hold {key}";
        }

        parts.Add(key);
        return string.Join(" + ", parts);
    }
}
