using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrivacyMask.App.Controls;

// PIN text is never data-bound to settings or a view model. Plaintext is present
// in the TextBox only while the user explicitly asks to reveal this field.
public partial class PinEntry : System.Windows.Controls.UserControl
{
    private bool _synchronizing;

    public PinEntry()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateInputNames();
        Unloaded += (_, _) => Clear();
        IsKeyboardFocusWithinChanged += (_, _) => Outline.BorderBrush =
            (System.Windows.Media.Brush)FindResource(IsKeyboardFocusWithin ? "FocusBrush" : "OutlineBrush");
    }

    public string Password
    {
        get => MaskedInput.Password;
        set => MaskedInput.Password = value;
    }

    public bool IsRevealed => RevealButton?.IsChecked == true;

    public void FocusInput() => (IsRevealed ? (System.Windows.Controls.Control)VisibleInput : MaskedInput).Focus();

    public void Clear()
    {
        RevealButton.IsChecked = false;
        MaskedInput.Clear();
        VisibleInput.Clear();
    }

    private void UpdateInputNames()
    {
        var name = AutomationProperties.GetName(this);
        AutomationProperties.SetName(MaskedInput, name);
        AutomationProperties.SetName(VisibleInput, name);
        AutomationProperties.SetName(RevealButton, $"{(IsRevealed ? "Hide" : "Show")} {name}");
    }

    private void Reveal_Changed(object sender, RoutedEventArgs e)
    {
        if (VisibleInput is null || MaskedInput is null) return;
        _synchronizing = true;
        VisibleInput.Text = IsRevealed ? MaskedInput.Password : string.Empty;
        VisibleInput.Visibility = IsRevealed ? Visibility.Visible : Visibility.Collapsed;
        MaskedInput.Visibility = IsRevealed ? Visibility.Collapsed : Visibility.Visible;
        _synchronizing = false;
        RevealButton.ToolTip = IsRevealed ? "Hide PIN" : "Show PIN";
        UpdateInputNames();
    }

    private void MaskedInput_Changed(object sender, RoutedEventArgs e)
    {
        if (!_synchronizing && IsRevealed) VisibleInput.Text = MaskedInput.Password;
    }

    private void VisibleInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_synchronizing || !IsRevealed) return;
        _synchronizing = true;
        MaskedInput.Password = VisibleInput.Text;
        _synchronizing = false;
    }
}
