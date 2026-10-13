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
    public string CompactTitle => Action switch
    {
        HotkeyAction.ToggleProtection => "Add / toggle active app",
        HotkeyAction.PanicHideAll => "Panic active app",
        HotkeyAction.IncreaseBlurStrength => "Increase blur",
        HotkeyAction.DecreaseBlurStrength => "Decrease blur",
        HotkeyAction.TemporaryRevealHold => "Hold to reveal",
        HotkeyAction.OpenSettings => "Open settings",
        _ => DisplayName,
    };
    public IReadOnlyList<ShortcutKeycap> GestureKeys
    {
        get
        {
            var keys = new List<string>();
            if (!IsHoldGesture)
            {
                if (Modifiers.HasFlag(HotkeyModifiers.Control)) keys.Add("Ctrl");
                if (Modifiers.HasFlag(HotkeyModifiers.Alt)) keys.Add("Alt");
                if (Modifiers.HasFlag(HotkeyModifiers.Shift)) keys.Add("Shift");
                if (Modifiers.HasFlag(HotkeyModifiers.Windows)) keys.Add("Win");
            }
            keys.Add(IsHoldGesture ? GestureText["Hold ".Length..] : GestureText.Split(" + ")[^1]);
            return keys.Select((text, index) => new ShortcutKeycap(text, index > 0)).ToArray();
        }
    }
    public string HoldDelayText => IsHoldGesture ? "(0.5 s)" : string.Empty;
    public bool HasPendingEnableChange => Enabled != _savedEnabled;
    public bool HasAttentionMessage => ShowRegistrationFailure || HasPendingEnableChange;
    public string AttentionHeading => ShowRegistrationFailure ? "Shortcut unavailable." : "Pending change.";
    public string AttentionDetail => ShowRegistrationFailure
        ? "Another program may be using this shortcut. Resolve the conflict and restart PrivacyMask."
        : "Save changes to apply this switch.";
    public string InformationText => $"{ScopeSummary}\n\n{RegistrationStatus}\n\nEnable/disable only; remapping is not available here.";

    public bool HasRegistrationFailure { get; }

    public bool ShowRegistrationFailure => Enabled && _savedEnabled && HasRegistrationFailure;
    public string RegistrationStatus => Enabled != _savedEnabled
        ? "Pending change — save to apply and check availability."
        : !Enabled ? "Disabled"
        : HasRegistrationFailure ? "Unavailable — close the conflicting app, then restart PrivacyMask to retry. Other shortcuts still work."
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
                RaisePropertyChanged(nameof(HasPendingEnableChange));
                RaisePropertyChanged(nameof(HasAttentionMessage));
                RaisePropertyChanged(nameof(AttentionHeading));
                RaisePropertyChanged(nameof(AttentionDetail));
                RaisePropertyChanged(nameof(InformationText));
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
