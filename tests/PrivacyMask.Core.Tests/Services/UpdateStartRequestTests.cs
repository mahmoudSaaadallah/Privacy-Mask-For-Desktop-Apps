using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateStartRequestTests
{
    private const string InstallId = "11111111111111111111111111111111";
    private const string OperationId = "22222222222222222222222222222222";

    [Theory]
    [InlineData("normal", UpdateStartMode.Normal)]
    [InlineData("minimized", UpdateStartMode.Minimized)]
    internal void Parse_accepts_the_exact_worker_contract(string mode, UpdateStartMode expectedMode)
    {
        var request = UpdateStartRequest.Parse(ValidArguments(mode));

        Assert.Equal(UpdateStartRequest.PipePrefix + "33333333333333333333333333333333", request.PipeName);
        Assert.Equal(InstallId, request.InstallId);
        Assert.Equal(OperationId, request.OperationId);
        Assert.Equal(42, request.ParentProcessId);
        Assert.Equal(638961234567890123, request.ParentCreationUtcTicks);
        Assert.Equal(expectedMode, request.Mode);
    }

    [Fact]
    public void IsRequested_recognizes_case_variants_so_malformed_attempts_fail_closed()
    {
        Assert.True(UpdateStartRequest.IsRequested(["--UPDATE-START"]));
        Assert.False(UpdateStartRequest.IsRequested(["--minimized"]));
        Assert.False(UpdateStartRequest.IsRequested([]));
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Parse_rejects_noncanonical_or_incomplete_requests(string[] arguments)
    {
        var error = Assert.Throws<ReleaseValidationException>(() => UpdateStartRequest.Parse(arguments));

        Assert.Equal("Invalid authenticated update-start request.", error.Message);
    }

    public static TheoryData<string[]> InvalidArguments()
    {
        var cases = new TheoryData<string[]>();
        cases.Add(Array.Empty<string>());
        cases.Add(["--UPDATE-START", Pipe(), InstallId, OperationId, "42", "638961234567890123", "normal"]);
        cases.Add(["--update-start"]);
        cases.Add(["--update-start", Pipe(), InstallId, OperationId, "42", "638961234567890123", "normal", "extra"]);
        cases.Add(["--update-start", "PrivacyMask.UpdateProbe.invalid", InstallId, OperationId, "42", "638961234567890123", "normal"]);
        cases.Add(["--update-start", Pipe(), "00000000000000000000000000000000", OperationId, "42", "638961234567890123", "normal"]);
        cases.Add(["--update-start", Pipe(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", OperationId, "42", "638961234567890123", "normal"]);
        cases.Add(["--update-start", Pipe(), InstallId, OperationId, "0", "638961234567890123", "normal"]);
        cases.Add(["--update-start", Pipe(), InstallId, OperationId, "042", "638961234567890123", "normal"]);
        cases.Add(["--update-start", Pipe(), InstallId, OperationId, "42", "+638961234567890123", "normal"]);
        cases.Add(["--update-start", Pipe(), InstallId, OperationId, "42", "638961234567890123", "Normal"]);
        return cases;
    }

    private static string[] ValidArguments(string mode) =>
        ["--update-start", Pipe(), InstallId, OperationId, "42", "638961234567890123", mode];

    private static string Pipe() =>
        UpdateStartRequest.PipePrefix + "33333333333333333333333333333333";
}
