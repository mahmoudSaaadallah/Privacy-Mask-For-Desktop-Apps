using System.Windows;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.App.Services;

namespace PrivacyMask.App.Windows;

public partial class PanicPinSetupWindow : Window
{
    private readonly PanicPinCredential? _currentCredential;

    public PanicPinSetupWindow(PanicPinCredential? currentCredential)
    {
        _currentCredential = PanicPinService.Clone(currentCredential);
        InitializeComponent();
        WindowWorkAreaSizer.Fit(this);
        if (_currentCredential is not null)
        {
            Title = "Change panic unlock PIN";
            HeadingText.Text = "Change panic PIN";
            DescriptionText.Text = "Enter the current PIN, then choose a new 4–8 digit PIN. Cancel keeps the current PIN. In Security, use Save changes after confirming this dialog to persist it.";
            CurrentPinPanel.Visibility = Visibility.Visible;
            CurrentPinBox.FocusInput();
        }
        else
        {
            NewPinBox.FocusInput();
        }
    }

    public PanicPinCredential? CreatedCredential { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        CurrentPinBox.Clear();
        NewPinBox.Clear();
        ConfirmPinBox.Clear();
        base.OnClosed(e);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (_currentCredential is not null
            && !PanicPinService.Verify(CurrentPinBox.Password, _currentCredential))
        {
            ErrorText.Text = "The current PIN is incorrect.";
            CurrentPinBox.Clear();
            CurrentPinBox.FocusInput();
            return;
        }

        if (!PanicPinService.IsValidPin(NewPinBox.Password))
        {
            ErrorText.Text = "Use 4 to 8 digits only.";
            NewPinBox.FocusInput();
            return;
        }

        if (!string.Equals(NewPinBox.Password, ConfirmPinBox.Password, StringComparison.Ordinal))
        {
            ErrorText.Text = "The new PIN and confirmation do not match.";
            ConfirmPinBox.Clear();
            ConfirmPinBox.FocusInput();
            return;
        }

        CreatedCredential = PanicPinService.Create(NewPinBox.Password);
        DialogResult = true;
        Close();
    }
}
