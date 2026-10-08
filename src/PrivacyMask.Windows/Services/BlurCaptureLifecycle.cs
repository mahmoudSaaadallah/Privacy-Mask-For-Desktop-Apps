namespace PrivacyMask.Windows.Services;

internal enum BlurCaptureCompletion
{
    Ignored,
    Current,
    Stale,
}

internal sealed class BlurCaptureLifecycle
{
    private int _currentGeneration;
    private int? _activeGeneration;

    public bool IsCaptureInProgress => _activeGeneration.HasValue;

    public void Invalidate()
    {
        _currentGeneration++;
    }

    public bool TryBegin(out int generation)
    {
        generation = _currentGeneration;
        if (_activeGeneration.HasValue)
        {
            return false;
        }

        _activeGeneration = generation;
        return true;
    }

    public BlurCaptureCompletion Complete(int generation)
    {
        if (_activeGeneration != generation)
        {
            return BlurCaptureCompletion.Ignored;
        }

        _activeGeneration = null;
        return generation == _currentGeneration
            ? BlurCaptureCompletion.Current
            : BlurCaptureCompletion.Stale;
    }

    public void Reset()
    {
        _currentGeneration++;
        _activeGeneration = null;
    }
}
