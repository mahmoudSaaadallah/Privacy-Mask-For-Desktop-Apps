using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ToolTip = System.Windows.Controls.ToolTip;

namespace PrivacyMask.App.Services;

public static class InformationHelp
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(InformationHelp), new PropertyMetadata(null, TextChanged));

    public static readonly DependencyProperty HoverOnlyProperty = DependencyProperty.RegisterAttached(
        "HoverOnly", typeof(bool), typeof(InformationHelp),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, HoverOnlyChanged));

    public static bool GetHoverOnly(DependencyObject element) => (bool)element.GetValue(HoverOnlyProperty);
    public static void SetHoverOnly(DependencyObject element, bool value) => element.SetValue(HoverOnlyProperty, value);

    public static bool OpensFromKeyboard(bool hoverOnly, Key key) => !hoverOnly && key is Key.F1 or Key.Enter or Key.Space;

    private static void HoverOnlyChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && element is FrameworkElement { ToolTip: ToolTip tip }) tip.IsOpen = false;
    }

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void TextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement control) return;
        AutomationProperties.SetHelpText(control, e.NewValue as string ?? string.Empty);
        if (control.ToolTip is not ToolTip tooltip)
        {
            tooltip = new ToolTip { PlacementTarget = control };
            if (control.TryFindResource("InformationToolTip") is Style style) tooltip.Style = style;
            control.ToolTip = tooltip;
        }
        tooltip.Content = new TextBlock { Text = e.NewValue as string, TextWrapping = TextWrapping.Wrap, MaxWidth = 336 };
        if (e.OldValue is null && e.NewValue is not null)
        {
            control.LostKeyboardFocus += Close;
            control.PreviewKeyDown += HelpKey;
            control.Unloaded += Unloaded;
            control.IsVisibleChanged += VisibilityChanged;
        }
        else if (e.NewValue is null)
        {
            control.LostKeyboardFocus -= Close;
            control.PreviewKeyDown -= HelpKey;
            control.Unloaded -= Unloaded;
            control.IsVisibleChanged -= VisibilityChanged;
            tooltip.IsOpen = false;
        }
    }

    private static void Close(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { ToolTip: ToolTip tip }) tip.IsOpen = false;
    }
    private static void Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ToolTip: ToolTip tip }) tip.IsOpen = false;
    }
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && sender is FrameworkElement { ToolTip: ToolTip tip }) tip.IsOpen = false;
    }
    private static void HelpKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is FrameworkElement { ToolTip: ToolTip tip } control && OpensFromKeyboard(GetHoverOnly(control), e.Key))
        {
            tip.IsOpen = true;
            e.Handled = true;
        }
    }

    public static bool CloseIfOpen(IInputElement? focused)
    {
        if (focused is FrameworkElement control && GetText(control) is not null
            && control.ToolTip is ToolTip { IsOpen: true } tip)
        {
            tip.IsOpen = false;
            return true;
        }
        return false;
    }
}
