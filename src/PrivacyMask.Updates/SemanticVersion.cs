using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PrivacyMask.Updates;

public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        @"\A(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly BigInteger _major, _minor, _patch;
    private readonly string[] _prerelease;
    public string ProductVersion { get; }
    public bool IsPrerelease => _prerelease.Length != 0;

    private SemanticVersion(string value, Match match)
    {
        ProductVersion = value.Split('+')[0];
        _major = BigInteger.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        _minor = BigInteger.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
        _patch = BigInteger.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture);
        _prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : [];
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(value) || value.Length > 256) return false;
        var match = Pattern.Match(value);
        if (!match.Success) return false;
        version = new(value, match);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        foreach (var comparison in new[] { _major.CompareTo(other._major), _minor.CompareTo(other._minor), _patch.CompareTo(other._patch) })
            if (comparison != 0) return comparison;
        if (!IsPrerelease || !other.IsPrerelease) return other.IsPrerelease.CompareTo(IsPrerelease);
        for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            var left = _prerelease[i];
            var right = other._prerelease[i];
            var leftNumeric = BigInteger.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue);
            var rightNumeric = BigInteger.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue);
            var comparison = leftNumeric && rightNumeric ? leftValue.CompareTo(rightValue)
                : leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1) : string.CompareOrdinal(left, right);
            if (comparison != 0) return comparison;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_major); hash.Add(_minor); hash.Add(_patch);
        foreach (var item in _prerelease) hash.Add(item, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
    public override string ToString() => ProductVersion;
}
