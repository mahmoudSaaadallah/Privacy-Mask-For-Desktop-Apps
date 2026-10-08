using System.Linq;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class AppSettingsValidatorTests
{
    [Fact]
    public void Validate_AcceptsDefaultSettings()
    {
        var settings = new DefaultSettingsFactory().Create();

        var errors = AppSettingsValidator.Validate(settings);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(79, 42, "Hover width")]
    [InlineData(1401, 42, "Hover width")]
    [InlineData(394, 19, "Hover height")]
    [InlineData(394, 421, "Hover height")]
    public void Validate_RejectsHoverRevealDimensionsOutsideSupportedRanges(
        int width,
        int height,
        string expectedLocation)
    {
        var settings = new DefaultSettingsFactory().Create();
        var profile = settings.AppProfiles[0];
        profile.HoverRevealWidthPixels = width;
        profile.HoverRevealHeightPixels = height;

        var error = Assert.Single(AppSettingsValidator.Validate(settings));

        Assert.Contains(expectedLocation, error.Location);
        Assert.Contains("pixels", error.Message);
    }

    [Fact]
    public void Validate_ReportsInvalidZoneFieldsWithTheirProfileAndZone()
    {
        var settings = new DefaultSettingsFactory().Create();
        var profile = settings.AppProfiles.Single(item => item.AppId == AppId.WhatsApp);
        var zone = profile.Zones[0];
        zone.DisplayName = " ";
        zone.RelativeRect = new RelativeRect(-0.1d, 0.8d, 1.2d, 0.4d);
        zone.Strength = 1.1d;

        var errors = AppSettingsValidator.Validate(settings);

        Assert.All(errors, error => Assert.Contains(profile.DisplayName, error.Location));
        Assert.Contains(errors, error => error.Message == "Enter a zone name.");
        Assert.Contains(errors, error => error.Message.StartsWith("X must be"));
        Assert.Contains(errors, error => error.Message.StartsWith("Width must be"));
        Assert.Contains(errors, error => error.Message.StartsWith("Y plus Height"));
        Assert.Contains(errors, error => error.Message.StartsWith("Surface intensity must be"));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_RejectsNonFiniteZoneValues(double value)
    {
        var settings = new DefaultSettingsFactory().Create();
        settings.AppProfiles[0].Zones[0].RelativeRect = new RelativeRect(value, 0d, 0.5d, 0.5d);

        var errors = AppSettingsValidator.Validate(settings);

        Assert.Contains(errors, error => error.Message.StartsWith("X must be"));
    }
}
