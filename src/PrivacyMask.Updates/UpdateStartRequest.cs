using System.Globalization;

namespace PrivacyMask.Updates;

internal enum UpdateStartMode
{
    Normal,
    Minimized
}

internal sealed record UpdateStartRequest(
    string PipeName,
    string InstallId,
    string OperationId,
    int ParentProcessId,
    long ParentCreationUtcTicks,
    UpdateStartMode Mode)
{
    internal const string Switch = "--update-start";
    internal const string PipePrefix = "PrivacyMask.UpdateProbe.";
    private const int ArgumentCount = 7;

    internal static bool IsRequested(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
            if (string.Equals(arguments[index], Switch, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static UpdateStartRequest Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != ArgumentCount || !string.Equals(arguments[0], Switch, StringComparison.Ordinal))
            throw Invalid();

        var pipeName = arguments[1];
        if (!pipeName.StartsWith(PipePrefix, StringComparison.Ordinal) ||
            !RecoveryRules.IsId(pipeName[PipePrefix.Length..]))
            throw Invalid();

        var installId = arguments[2];
        var operationId = arguments[3];
        if (!RecoveryRules.IsId(installId) || !RecoveryRules.IsId(operationId))
            throw Invalid();

        if (!TryParseCanonicalPositiveInt32(arguments[4], out var parentProcessId) ||
            !TryParseCanonicalPositiveInt64(arguments[5], out var parentCreationUtcTicks))
            throw Invalid();

        var mode = arguments[6] switch
        {
            "normal" => UpdateStartMode.Normal,
            "minimized" => UpdateStartMode.Minimized,
            _ => throw Invalid()
        };

        return new(pipeName, installId, operationId, parentProcessId, parentCreationUtcTicks, mode);
    }

    private static bool TryParseCanonicalPositiveInt32(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) &&
        result > 0 && result.ToString(CultureInfo.InvariantCulture) == value;

    private static bool TryParseCanonicalPositiveInt64(string value, out long result) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) &&
        result > 0 && result.ToString(CultureInfo.InvariantCulture) == value;

    private static ReleaseValidationException Invalid() =>
        new("Invalid authenticated update-start request.");
}
