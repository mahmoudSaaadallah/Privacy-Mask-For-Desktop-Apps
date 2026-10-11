namespace PrivacyMask.Core.Models;

public sealed class PanicPinCredential
{
    public int Iterations { get; init; }

    public string Salt { get; init; } = string.Empty;

    public string Hash { get; init; } = string.Empty;
}
