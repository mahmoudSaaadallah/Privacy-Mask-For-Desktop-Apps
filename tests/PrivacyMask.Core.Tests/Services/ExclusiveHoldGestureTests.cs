using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ExclusiveHoldGestureTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Update_ActivatesOnlyAfterExclusiveHoldDuration()
    {
        var gesture = new ExclusiveHoldGesture(TimeSpan.FromSeconds(1.5d));

        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start));
        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start.AddMilliseconds(1499d)));
        Assert.True(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start.AddMilliseconds(1500d)));
    }

    [Fact]
    public void Update_OtherKeyCancelsCurrentHoldEvenAfterOtherKeyIsReleased()
    {
        var gesture = new ExclusiveHoldGesture(TimeSpan.FromSeconds(1.5d));

        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start));
        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: true, Start.AddMilliseconds(500d)));
        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start.AddSeconds(3d)));
    }

    [Fact]
    public void Update_ReleaseAllowsANewExclusiveHold()
    {
        var gesture = new ExclusiveHoldGesture(TimeSpan.FromSeconds(1.5d));
        gesture.Update(gestureKeyDown: true, anotherKeyDown: true, Start);

        Assert.False(gesture.Update(gestureKeyDown: false, anotherKeyDown: false, Start.AddSeconds(1d)));
        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start.AddSeconds(2d)));
        Assert.True(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start.AddSeconds(3.5d)));
    }

    [Fact]
    public void Reset_RequiresFullHoldDurationAgain()
    {
        var gesture = new ExclusiveHoldGesture(TimeSpan.FromSeconds(1.5d));
        gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start);
        gesture.Reset();

        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start.AddSeconds(2d)));
        Assert.False(gesture.Update(gestureKeyDown: true, anotherKeyDown: false, Start.AddSeconds(3d)));
    }
}
