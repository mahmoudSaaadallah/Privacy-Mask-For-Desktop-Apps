using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateRestartBlockTests
{
    [Fact]
    public void DeduplicatesByProfileIdentityNotDisplayNameAndBoundsVisibleNames()
    {
        var block = UpdateRestartBlock.Assess(false, false, () => new[]
        {
            Window("a", "Same name"), Window("A", "Same name"), Window("b", "Same name"),
            Window("c", "Third"), Window("d", "Fourth"), Window("e", "Fifth"),
        });
        Assert.Equal(UpdateRestartBlockKind.VisibleWindows, block!.Kind);
        Assert.Equal(5, block.Applications.Count);
        Assert.Equal(2, block.Message.Split("Same name").Length - 1);
        Assert.Contains("Third", block.Message);
        Assert.Contains("+2 more applications", block.Message);
        Assert.DoesNotContain("Fourth", block.Message);
        Assert.True(block.CanReviewApplications);
    }

    [Fact]
    public void EveryVisibleMatchingWindowBlocksRegardlessOfPausedProtection()
    {
        // Presentation accepts no pause flag: pause is not evidence of a minimized window.
        var visible = Window("paused-profile", "Paused app");
        Assert.NotNull(UpdateRestartBlock.Assess(false, false, () => [visible, visible with { IsMinimized = true }]));
        Assert.Null(UpdateRestartBlock.Assess(false, false, () => [visible with { IsMinimized = true }]));
        Assert.Null(UpdateRestartBlock.Assess(false, false, () => [visible with { IsVisible = false }, new(true, false, null)]));
    }

    [Fact]
    public void EnumerationFailureNeverMeansPermissionToRestart()
    {
        static IEnumerable<UpdateBlockingWindow> Partial()
        {
            yield return Window("a", "Example");
            throw new InvalidOperationException("fixture");
        }
        var block = UpdateRestartBlock.Assess(false, false, Partial);
        Assert.Equal(UpdateRestartBlockKind.InspectionUnavailable, block!.Kind);
        Assert.Empty(block.Applications);
        Assert.False(block.CanReviewApplications);
    }

    [Theory]
    [InlineData(true, true, (int)UpdateRestartBlockKind.Panic)]
    [InlineData(false, true, (int)UpdateRestartBlockKind.SettingsEdits)]
    public void PanicAndEditsHavePriorityWithoutInspectingWindows(bool panic, bool edits, int expected)
    {
        var block = UpdateRestartBlock.Assess(panic, edits, () => throw new Exception("Must not inspect"));
        Assert.Equal((UpdateRestartBlockKind)expected, block!.Kind);
    }

    [Fact]
    public void LongOrMultilineNamesAreBoundedPlainText()
    {
        var block = UpdateRestartBlock.Assess(false, false, () => [Window("a", new string('x', 500) + "\r\nsecret")]);
        Assert.True(block!.Message.Length < 400);
        Assert.DoesNotContain("secret", block.Message);
        Assert.DoesNotContain('\n', block.Message);
    }

    private static UpdateBlockingWindow Window(string id, string name) => new(true, false, new(id, name));
}
