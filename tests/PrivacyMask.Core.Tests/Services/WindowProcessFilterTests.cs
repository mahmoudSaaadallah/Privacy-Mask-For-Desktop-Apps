using System.Collections.Generic;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class WindowProcessFilterTests
{
    [Theory]
    [InlineData("WhatsApp", true)]
    [InlineData("whatsapp", true)]
    [InlineData("WhatsApp.exe", true)]
    [InlineData("WhatsAppBeta", true)]
    [InlineData("Telegram", true)]
    [InlineData("Notepad", false)]
    public void IsMatch_UsesEnabledProfileProcessNames(string processName, bool expected)
    {
        var filter = new WindowProcessFilter(new DefaultSettingsFactory().Create().AppProfiles);

        Assert.Equal(expected, filter.IsMatch(processName));
    }

    [Fact]
    public void IsMatch_IgnoresDisabledProfiles()
    {
        var profiles = new DefaultSettingsFactory().Create().AppProfiles;
        foreach (var profile in profiles)
        {
            profile.Enabled = false;
        }

        var filter = new WindowProcessFilter(profiles);

        Assert.False(filter.IsMatch("WhatsApp"));
        Assert.False(filter.IsMatch("Telegram"));
    }

    [Fact]
    public void IsMatch_AllowsTitleOrClassOnlyMatchersToInspectEveryProcess()
    {
        var profiles = new List<AppProfile>
        {
            new()
            {
                Enabled = true,
                WindowMatchers =
                [
                    new WindowMatcher
                    {
                        TitleContains = "Private workspace",
                    },
                ],
            },
        };

        var filter = new WindowProcessFilter(profiles);

        Assert.True(filter.MatchesAllProcesses);
        Assert.True(filter.IsMatch("AnyProcess"));
    }

    [Fact]
    public void IsMatch_CustomProfilesDoNotMatchSimilarProcessNames()
    {
        var profile = CustomAppProfileFactory.Create("Notes", "Notepad", "custom-notes");
        var filter = new WindowProcessFilter([profile]);

        Assert.True(filter.IsMatch("Notepad"));
        Assert.False(filter.IsMatch("NotepadPlus"));
        Assert.False(filter.IsMatch("Note"));
    }
}
