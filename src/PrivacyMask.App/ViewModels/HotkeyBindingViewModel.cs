using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.ViewModels;

public sealed class HotkeyBindingViewModel : ObservableObject
{
    private bool _enabled;
    private readonly bool _savedEnabled;

    public HotkeyBindingViewModel(HotkeyBinding binding, bool hasRegistrationFailure)
    {
        Action = binding.Action;
        DisplayName = binding.DisplayName;
        Modifiers = binding.Modifiers;
        VirtualKey = binding.VirtualKey;
        IsHoldGesture = binding.IsHoldGesture;
        _enabled = binding.Enabled;
        _savedEnabled = binding.Enabled;
        GestureText = HotkeyDisplayFormatter.Format(binding);
        HasRegistrationFailure = hasRegistrationFailure;
    }

    public HotkeyAction Action { get; }

    public string DisplayName { get; }

    public HotkeyModifiers Modifiers { get; }

    public int VirtualKey { get; }

    public bool IsHoldGesture { get; }

    public string GestureText { get; }

    public bool HasRegistrationFailure { get; }

    public bool ShowRegistrationFailure => Enabled && _savedEnabled && HasRegistrationFailure;
    public string RegistrationStatus => Enabled != _savedEnabled
        ? "Pending change — save to apply and check availability."
        : !Enabled ? "Disabled"
        : HasRegistrationFailure ? "Unavailable — close the conflicting app, then save to retry. Other shortcuts still work."
        : IsHoldGesture ? "Hold gesture enabled" : "Registered";
    public string ScopeSummary => Action switch
    {
        HotkeyAction.ToggleProtection => "Focused application only; adds an unconfigured desktop app with default settings.",
        HotkeyAction.PanicHideAll => "Focused application only; a PIN is required to remove its panic mask.",
        HotkeyAction.IncreaseBlurStrength => "Focused protected application only; increases blur by 5%.",
        HotkeyAction.DecreaseBlurStrength => "Focused protected application only; decreases blur by 5%.",
        HotkeyAction.TemporaryRevealHold => "Focused protected application only. Hold the key alone for 0.5 seconds; another key cancels reveal until release. Panic masks cannot be revealed.",
        HotkeyAction.OpenSettings => "Opens PrivacyMask settings, regardless of which application is focused.",
        _ => "Global shortcut",
    };

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                RaisePropertyChanged(nameof(RegistrationStatus));
                RaisePropertyChanged(nameof(ShowRegistrationFailure));
            }
        }
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
