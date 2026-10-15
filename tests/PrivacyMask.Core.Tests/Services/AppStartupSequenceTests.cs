using PrivacyMask.App.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class AppStartupSequenceTests
{
    [Fact]
    public async Task Update_readiness_follows_shell_initialization_and_precedes_interactive_UI()
    {
        var milestones = new List<string>();

        await AppStartupSequence.RunAsync(
            () => Add("shell"),
            () => Add("committed"),
            () => Add("interactive"));

        Assert.Equal(["shell", "committed", "interactive"], milestones);
        Task Add(string milestone) { milestones.Add(milestone); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Failed_shell_initialization_never_signals_readiness_or_opens_UI()
    {
        var laterMilestones = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => AppStartupSequence.RunAsync(
            () => throw new InvalidOperationException("synthetic startup failure"),
            () => { laterMilestones++; return Task.CompletedTask; },
            () => { laterMilestones++; return Task.CompletedTask; }));

        Assert.Equal(0, laterMilestones);
    }

    [Fact]
    public async Task Failed_update_confirmation_never_opens_interactive_UI()
    {
        var interactive = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => AppStartupSequence.RunAsync(
            () => Task.CompletedTask,
            () => throw new InvalidOperationException("synthetic readiness failure"),
            () => { interactive = true; return Task.CompletedTask; }));

        Assert.False(interactive);
    }
}
