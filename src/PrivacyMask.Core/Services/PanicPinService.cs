using System.Security.Cryptography;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class PanicPinService
{
    public const int MinimumPinLength = 4;
    public const int MaximumPinLength = 8;
    public const int DefaultIterations = 210_000;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 1_000_000;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public static bool IsValidPin(string? pin)
    {
        return pin is not null
            && pin.Length is >= MinimumPinLength and <= MaximumPinLength
            && pin.All(char.IsAsciiDigit);
    }

    public static PanicPinCredential Create(string pin)
    {
        if (!IsValidPin(pin))
        {
            throw new ArgumentException(
                $"PIN must contain {MinimumPinLength} to {MaximumPinLength} digits.",
                nameof(pin));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            pin!,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashLength);
        return new PanicPinCredential
        {
            Iterations = DefaultIterations,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
        };
    }

    public static bool Verify(string? pin, PanicPinCredential? credential)
    {
        if (!IsValidPin(pin) || !TryDecode(credential, out var salt, out var expectedHash))
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            pin!,
            salt,
            credential!.Iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public static bool IsCredentialValid(PanicPinCredential? credential)
    {
        return TryDecode(credential, out _, out _);
    }

    public static PanicPinCredential? Clone(PanicPinCredential? credential)
    {
        return credential is null
            ? null
            : new PanicPinCredential
            {
                Iterations = credential.Iterations,
                Salt = credential.Salt,
                Hash = credential.Hash,
            };
    }

    private static bool TryDecode(
        PanicPinCredential? credential,
        out byte[] salt,
        out byte[] hash)
    {
        salt = [];
        hash = [];
        if (credential is null
            || credential.Iterations is < MinimumIterations or > MaximumIterations
            || string.IsNullOrWhiteSpace(credential.Salt)
            || string.IsNullOrWhiteSpace(credential.Hash))
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(credential.Salt);
            hash = Convert.FromBase64String(credential.Hash);
            return salt.Length == SaltLength && hash.Length == HashLength;
        }
        catch (FormatException)
        {
            salt = [];
            hash = [];
            return false;
        }
    }
}
