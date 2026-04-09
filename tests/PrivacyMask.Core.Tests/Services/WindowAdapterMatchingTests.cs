using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Adapters;

namespace PrivacyMask.Core.Tests.Services;

public sealed class WindowAdapterMatchingTests
{
    [Fact]
    public void WhatsAppAdapter_MatchesOfficialStoreProcessVariant()
    {
        var adapter = new WhatsAppWindowAdapter();
        var profile = new AppProfile
        {
            AppId = AppId.WhatsApp,
            DisplayName = "WhatsApp Desktop",
            WindowMatchers =
            [
                new WindowMatcher
                {
                    ProcessNames = ["WhatsApp"],
                },
            ],
        };

        var snapshot = new WindowSnapshot
        {
            Handle = 1,
            ProcessName = "WhatsApp.Root",
            Title = "WhatsApp",
            ClassName = "WinUIDesktopWin32WindowClass",
            Bounds = new ScreenRect(0, 0, 1200, 900),
            IsVisible = true,
            IsForeground = true,
            IsMinimized = false,
        };

        var matches = adapter.IsMatch(snapshot, profile);

        Assert.True(matches);
    }
}
