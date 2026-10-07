using System.Collections.Generic;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.Services;

public sealed record HotkeyRegistrationFailure(
    HotkeyAction Action,
    string DisplayName,
    int NativeErrorCode);

public sealed record HotkeyRegistrationResult(
    int AttemptedCount,
    int RegisteredCount,
    IReadOnlyList<HotkeyRegistrationFailure> Failures)
{
    public bool HasFailures => Failures.Count > 0;
}
