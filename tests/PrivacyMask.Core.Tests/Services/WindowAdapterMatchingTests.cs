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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void WhatsAppAdapter_RejectsWindowsThatCannotBeProtected(bool isVisible, bool isMinimized)
    {
        var adapter = new WhatsAppWindowAdapter();
        var profile = CreateWhatsAppProfile(new WindowMatcher
        {
            ProcessNames = ["WhatsApp"],
        });
        var snapshot = CreateSnapshot(
            processName: "WhatsApp",
            title: "WhatsApp",
            className: "WinUIDesktopWin32WindowClass",
            isVisible: isVisible,
            isMinimized: isMinimized);

        Assert.False(adapter.IsMatch(snapshot, profile));
    }

    [Fact]
    public void WhatsAppAdapter_RequiresEveryConfiguredMatcherConstraint()
    {
        var adapter = new WhatsAppWindowAdapter();
        var profile = CreateWhatsAppProfile(new WindowMatcher
        {
            ProcessNames = ["WhatsApp"],
            TitleContains = "WhatsApp",
            ClassNameContains = "WinUIDesktop",
        });

        var matching = CreateSnapshot("WhatsApp", "WhatsApp", "WinUIDesktopWin32WindowClass");
        var wrongTitle = CreateSnapshot("WhatsApp", "Settings", "WinUIDesktopWin32WindowClass");
        var wrongClass = CreateSnapshot("WhatsApp", "WhatsApp", "ApplicationFrameWindow");

        Assert.True(adapter.IsMatch(matching, profile));
        Assert.False(adapter.IsMatch(wrongTitle, profile));
        Assert.False(adapter.IsMatch(wrongClass, profile));
    }

    [Fact]
    public void TelegramAdapter_SelectsPresetFromCurrentWindowWidth()
    {
        var factory = new PrivacyMask.Core.Services.DefaultSettingsFactory();
        var profile = factory.Create().AppProfiles.Single(candidate => candidate.AppId == AppId.Telegram);
        var adapter = new TelegramWindowAdapter();

        var compact = adapter.SelectPreset(
            CreateSnapshot("Telegram", "Telegram", "QtWindow", width: 919),
            profile,
            profile.Presets);
        var wide = adapter.SelectPreset(
            CreateSnapshot("Telegram", "Telegram", "QtWindow", width: 920),
            profile,
            profile.Presets);

        Assert.Equal("telegram-compact", compact.PresetId);
        Assert.Equal("telegram-wide", wide.PresetId);
    }

    [Fact]
    public void GenericAdapter_MatchesOnlyTheExactConfiguredProcess()
    {
        var adapter = new GenericWindowAdapter();
        var profile = PrivacyMask.Core.Services.CustomAppProfileFactory.Create("Notes", "Notepad", "custom-notes");

        Assert.True(adapter.IsMatch(CreateSnapshot("Notepad", "Notes", "Notepad"), profile));
        Assert.False(adapter.IsMatch(CreateSnapshot("NotepadPlus", "Notes", "Notepad"), profile));
    }

    [Fact]
    public void GenericAdapter_SelectsTheCustomFullWindowPreset()
    {
        var adapter = new GenericWindowAdapter();
        var profile = PrivacyMask.Core.Services.CustomAppProfileFactory.Create("Notes", "Notepad", "custom-notes");

        var preset = adapter.SelectPreset(
            CreateSnapshot("Notepad", "Notes", "Notepad"),
            profile,
            profile.Presets);

        Assert.Equal("custom-full-window", preset.PresetId);
        Assert.Equal(new RelativeRect(0d, 0d, 1d, 1d), preset.Zones.Single().RelativeRect);
    }

    private static AppProfile CreateWhatsAppProfile(WindowMatcher matcher)
    {
        return new AppProfile
        {
            AppId = AppId.WhatsApp,
            DisplayName = "WhatsApp Desktop",
            WindowMatchers = [matcher],
        };
    }

    private static WindowSnapshot CreateSnapshot(
        string processName,
        string title,
        string className,
        bool isVisible = true,
        bool isMinimized = false,
        int width = 1200)
    {
        return new WindowSnapshot
        {
            Handle = 1,
            ProcessName = processName,
            Title = title,
            ClassName = className,
            Bounds = new ScreenRect(0, 0, width, 900),
            IsVisible = isVisible,
            IsForeground = true,
            IsMinimized = isMinimized,
        };
    }
}
