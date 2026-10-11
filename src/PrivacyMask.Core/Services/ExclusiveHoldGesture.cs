namespace PrivacyMask.Core.Services;

public sealed class ExclusiveHoldGesture
{
    private readonly TimeSpan _holdDuration;
    private DateTimeOffset? _pressedAt;
    private bool _wasCanceled;

    public ExclusiveHoldGesture(TimeSpan holdDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(holdDuration, TimeSpan.Zero);
        _holdDuration = holdDuration;
    }

    public bool Update(bool gestureKeyDown, bool anotherKeyDown, DateTimeOffset now)
    {
        if (!gestureKeyDown)
        {
            Reset();
            return false;
        }

        _pressedAt ??= now;
        _wasCanceled |= anotherKeyDown;
        return !_wasCanceled && now - _pressedAt.Value >= _holdDuration;
    }

    public void Reset()
    {
        _pressedAt = null;
        _wasCanceled = false;
    }
}
