using System.Text;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class PreparedSettingsSchemaTests
{
    [Theory]
    [InlineData("{\"version\":13}", 13)]
    [InlineData("{\"Version\":12,\"other\":true}", 12)]
    public void WorkerUsesActualSettingsSchemaInsteadOfAssumingTwelve(string json, int expected) =>
        Assert.Equal(expected, PreparedSettingsSchema.Parse(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":0}")]
    [InlineData("{\"version\":\"13\"}")]
    [InlineData("{\"version\":12,\"Version\":13}")]
    [InlineData("[]")]
    public void MissingInvalidOrAmbiguousSchemaNeverInventsCompatibility(string json) =>
        Assert.ThrowsAny<Exception>(() => PreparedSettingsSchema.Parse(Encoding.UTF8.GetBytes(json)));
}
