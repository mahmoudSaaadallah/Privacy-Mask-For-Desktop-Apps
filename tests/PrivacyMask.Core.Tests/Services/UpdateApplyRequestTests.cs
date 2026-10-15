using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateApplyRequestTests
{
    private const string InstallId = "11111111111111111111111111111111";
    private const string OperationId = "22222222222222222222222222222222";

    [Theory]
    [InlineData("normal", UpdateStartMode.Normal)]
    [InlineData("minimized", UpdateStartMode.Minimized)]
    internal void ParseAndFormatPreserveTheExactContract(string mode, UpdateStartMode expectedMode)
    {
        var arguments = ValidArguments(mode);
        var request = UpdateApplyRequest.Parse(arguments);

        Assert.Equal(UpdateHandoffServer.PipePrefix + "33333333333333333333333333333333",
            request.PipeName);
        Assert.Equal(InstallId, request.InstallId);
        Assert.Equal(OperationId, request.OperationId);
        Assert.Equal(42, request.ApplicationProcessId);
        Assert.Equal(638961234567890123, request.ApplicationCreationUtcTicks);
        Assert.Equal(expectedMode, request.Mode);
        Assert.Equal(arguments, request.ToArguments());
    }

    [Fact]
    public void IsRequestedRecognizesMalformedCaseVariantsForFailClosedDispatch()
    {
        Assert.True(UpdateApplyRequest.IsRequested(["--UPDATE-APPLY"]));
        Assert.True(UpdateApplyRequest.IsRequested(["--minimized", "--update-apply"]));
        Assert.False(UpdateApplyRequest.IsRequested(["--minimized"]));
        Assert.False(UpdateApplyRequest.IsRequested([]));
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void ParseRejectsNoncanonicalOrIncompleteRequests(string[] arguments)
    {
        var error = Assert.Throws<ReleaseValidationException>(() => UpdateApplyRequest.Parse(arguments));

        Assert.Equal("Invalid authenticated update-apply request.", error.Message);
    }

    [Fact]
    public void FormatRejectsAnInvalidInMemoryRequest()
    {
        var request = UpdateApplyRequest.Parse(ValidArguments("normal")) with
        {
            ApplicationProcessId = 0
        };

        Assert.Throws<ReleaseValidationException>(() => request.ToArguments());
    }

    public static TheoryData<string[]> InvalidArguments()
    {
        var cases = new TheoryData<string[]>();
        cases.Add(Array.Empty<string>());
        cases.Add(["--UPDATE-APPLY", Pipe(), InstallId, OperationId, "42", "638961234567890123", "normal"]);
        cases.Add(["--update-apply"]);
        cases.Add(["--update-apply", Pipe(), InstallId, OperationId, "42", "638961234567890123", "normal", "extra"]);
        cases.Add(["--update-apply", "PrivacyMask.UpdateHandoff.invalid", InstallId, OperationId, "42", "638961234567890123", "normal"]);
        cases.Add(["--update-apply", Pipe(), "00000000000000000000000000000000", OperationId, "42", "638961234567890123", "normal"]);
        cases.Add(["--update-apply", Pipe(), InstallId, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "42", "638961234567890123", "normal"]);
        cases.Add(["--update-apply", Pipe(), InstallId, OperationId, "0", "638961234567890123", "normal"]);
        cases.Add(["--update-apply", Pipe(), InstallId, OperationId, "042", "638961234567890123", "normal"]);
        cases.Add(["--update-apply", Pipe(), InstallId, OperationId, "42", "+638961234567890123", "normal"]);
        cases.Add(["--update-apply", Pipe(), InstallId, OperationId, "42", "638961234567890123", "Normal"]);
        return cases;
    }

    private static string[] ValidArguments(string mode) =>
        ["--update-apply", Pipe(), InstallId, OperationId, "42", "638961234567890123", mode];

    private static string Pipe() =>
        UpdateHandoffServer.PipePrefix + "33333333333333333333333333333333";
}
