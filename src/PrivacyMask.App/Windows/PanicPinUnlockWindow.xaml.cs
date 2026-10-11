using System.Windows;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.App.Windows;

public partial class PanicPinUnlockWindow : Window
{
    private readonly PanicPinCredential _credential;

    public PanicPinUnlockWindow(PanicPinCredential credential, string targetName)
    {
        _credential = PanicPinService.Clone(credential)
            ?? throw new ArgumentNullException(nameof(credential));
        InitializeComponent();
        DescriptionText.Text = $"Enter the panic unlock PIN to reveal {targetName}.";
        PinBox.Focus();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (!PanicPinService.Verify(PinBox.Password, _credential))
        {
            ErrorText.Text = "Incorrect PIN. The panic mask remains active.";
            PinBox.Clear();
            PinBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }
}
