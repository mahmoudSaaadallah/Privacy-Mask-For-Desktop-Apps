using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.ViewModels;

public sealed class HotkeyBindingViewModel : ObservableObject
{
    private bool _enabled;

    public HotkeyBindingViewModel(HotkeyBinding binding)
    {
        Action = binding.Action;
        DisplayName = binding.DisplayName;
        Modifiers = binding.Modifiers;
        VirtualKey = binding.VirtualKey;
        IsHoldGesture = binding.IsHoldGesture;
        _enabled = binding.Enabled;
        GestureText = HotkeyDisplayFormatter.Format(binding);
    }

    public HotkeyAction Action { get; }

    public string DisplayName { get; }

    public HotkeyModifiers Modifiers { get; }

    public int VirtualKey { get; }

    public bool IsHoldGesture { get; }

    public string GestureText { get; }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public HotkeyBinding ToModel()
    {
        return new HotkeyBinding
        {
            Action = Action,
            DisplayName = DisplayName,
            Modifiers = Modifiers,
            VirtualKey = VirtualKey,
            Enabled = Enabled,
            IsHoldGesture = IsHoldGesture,
        };
    }
}
