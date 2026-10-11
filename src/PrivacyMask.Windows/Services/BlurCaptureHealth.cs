namespace PrivacyMask.Windows.Services;

internal sealed class BlurCaptureHealth
{
    private readonly int _maximumConsecutiveFailures;
    private int _consecutiveFailures;

    public BlurCaptureHealth(int maximumConsecutiveFailures = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConsecutiveFailures, 1);
        _maximumConsecutiveFailures = maximumConsecutiveFailures;
    }

    public bool IsDisabled { get; private set; }

    public bool RecordFailure()
    {
        if (IsDisabled)
        {
            return true;
        }

        _consecutiveFailures++;
        IsDisabled = _consecutiveFailures >= _maximumConsecutiveFailures;
        return IsDisabled;
    }

    public void RecordTimeout()
    {
        IsDisabled = true;
    }

    public void RecordSuccess()
    {
        if (IsDisabled)
        {
            return;
        }

        _consecutiveFailures = 0;
    }
}
