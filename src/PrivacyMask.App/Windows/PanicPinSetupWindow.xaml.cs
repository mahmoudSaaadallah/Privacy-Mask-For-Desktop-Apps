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
            DescriptionText.Text = "Enter the current PIN, then choose a new 4–8 digit PIN.";
            CurrentPinPanel.Visibility = Visibility.Visible;
            CurrentPinBox.Focus();
        }
        else
        {
            NewPinBox.Focus();
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
            CurrentPinBox.Focus();
            return;
        }

        if (!PanicPinService.IsValidPin(NewPinBox.Password))
        {
            ErrorText.Text = "Use 4 to 8 digits only.";
            NewPinBox.Focus();
            return;
        }

        if (!string.Equals(NewPinBox.Password, ConfirmPinBox.Password, StringComparison.Ordinal))
        {
            ErrorText.Text = "The new PIN and confirmation do not match.";
            ConfirmPinBox.Clear();
            ConfirmPinBox.Focus();
            return;
        }

        CreatedCredential = PanicPinService.Create(NewPinBox.Password);
        DialogResult = true;
        Close();
    }
}
