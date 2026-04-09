using System.Collections.Generic;

namespace PrivacyMask.Core.Models;

public sealed class WindowMatcher
{
    public List<string> ProcessNames { get; init; } = [];

    public string? TitleContains { get; init; }

    public string? ClassNameContains { get; init; }
}
