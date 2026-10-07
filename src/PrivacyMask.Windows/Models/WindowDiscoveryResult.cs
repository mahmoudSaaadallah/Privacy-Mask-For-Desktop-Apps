using System.Collections.Generic;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Windows.Models;

public sealed class WindowDiscoveryResult
{
    public IReadOnlyList<WindowSnapshot> Windows { get; init; } = [];

    public IReadOnlyList<WindowSnapshot> CandidateWindows { get; init; } = [];

    public nint ForegroundHandle { get; init; }
}
