using System.Windows.Input;
using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class InformationHelpPolicyTests
{
    [Theory]
    [InlineData(true, Key.F1, false)]
    [InlineData(true, Key.Enter, false)]
    [InlineData(true, Key.Space, false)]
    [InlineData(true, Key.Tab, false)]
    [InlineData(true, Key.Escape, false)]
    [InlineData(false, Key.F1, true)]
    [InlineData(false, Key.Enter, true)]
    [InlineData(false, Key.Space, true)]
    [InlineData(false, Key.Tab, false)]
    [InlineData(false, Key.Escape, false)]
    public void HoverOnlyPages_DoNotOpenKeyboardHelpWhileOtherPagesRetainExplicitKeys(bool hoverOnly, Key key, bool expected)
    {
        Assert.Equal(expected, InformationHelp.OpensFromKeyboard(hoverOnly, key));
    }
}
