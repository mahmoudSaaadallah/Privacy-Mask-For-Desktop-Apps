namespace PrivacyMask.Core.Services;

public sealed class ProfileProtectionState
{
    private readonly HashSet<string> _pausedProfileIds = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPaused(string profileId)
    {
        return !string.IsNullOrWhiteSpace(profileId) && _pausedProfileIds.Contains(profileId);
    }

    public bool Toggle(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (_pausedProfileIds.Add(profileId))
        {
            return true;
        }

        _pausedProfileIds.Remove(profileId);
        return false;
    }

    public void Prune(IEnumerable<string> profileIds)
    {
        var activeProfileIds = profileIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _pausedProfileIds.RemoveWhere(profileId => !activeProfileIds.Contains(profileId));
    }
}
