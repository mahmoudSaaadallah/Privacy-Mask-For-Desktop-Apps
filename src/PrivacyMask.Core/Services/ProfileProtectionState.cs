using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public sealed class ProfileProtectionState
{
    private readonly HashSet<string> _pausedProfileIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _panicProfileIds = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPaused(string profileId)
    {
        return !string.IsNullOrWhiteSpace(profileId) && _pausedProfileIds.Contains(profileId);
    }

    public bool IsPanic(string profileId)
    {
        return !string.IsNullOrWhiteSpace(profileId) && _panicProfileIds.Contains(profileId);
    }

    public bool TogglePause(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (_pausedProfileIds.Add(profileId))
        {
            return true;
        }

        _pausedProfileIds.Remove(profileId);
        return false;
    }

    public bool TogglePanic(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (_panicProfileIds.Add(profileId))
        {
            return true;
        }

        _panicProfileIds.Remove(profileId);
        return false;
    }

    public RuntimeMode ResolveMode(string profileId, RuntimeMode globalMode)
    {
        return globalMode == RuntimeMode.Panic || IsPanic(profileId)
            ? RuntimeMode.Panic
            : globalMode;
    }

    public void Prune(IEnumerable<string> profileIds)
    {
        var activeProfileIds = profileIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _pausedProfileIds.RemoveWhere(profileId => !activeProfileIds.Contains(profileId));
        _panicProfileIds.RemoveWhere(profileId => !activeProfileIds.Contains(profileId));
    }
}
