using System.Text;

namespace PrivacyMask.Core.Services;

public static class ProcessNameMatcher
{
    public static bool Matches(string candidate, string actual)
    {
        if (string.Equals(candidate, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedCandidate = Normalize(candidate);
        var normalizedActual = Normalize(actual);

        return normalizedActual.StartsWith(normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedActual, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string processName)
    {
        var builder = new StringBuilder(processName.Length);
        foreach (var character in processName)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
