using System.Collections.Generic;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public sealed class WindowProcessFilter
{
    private readonly HashSet<string> _exactProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _normalizedProcessNames = [];

    public WindowProcessFilter(IEnumerable<AppProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            if (!profile.Enabled)
            {
                continue;
            }

            foreach (var matcher in profile.WindowMatchers)
            {
                if (matcher.ProcessNames.Count == 0)
                {
                    MatchesAllProcesses = true;
                    return;
                }

                foreach (var processName in matcher.ProcessNames)
                {
                    if (string.IsNullOrWhiteSpace(processName) || !_exactProcessNames.Add(processName))
                    {
                        continue;
                    }

                    _normalizedProcessNames.Add(ProcessNameMatcher.Normalize(processName));
                }
            }
        }
    }

    public bool MatchesAllProcesses { get; }

    public bool IsMatch(string processName)
    {
        if (MatchesAllProcesses)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        if (_exactProcessNames.Contains(processName))
        {
            return true;
        }

        var normalizedActual = ProcessNameMatcher.Normalize(processName);
        foreach (var normalizedCandidate in _normalizedProcessNames)
        {
            if (normalizedActual.StartsWith(normalizedCandidate, StringComparison.OrdinalIgnoreCase)
                || normalizedCandidate.StartsWith(normalizedActual, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
