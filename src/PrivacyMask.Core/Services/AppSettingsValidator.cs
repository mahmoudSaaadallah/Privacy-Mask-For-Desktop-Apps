using System;
using System.Collections.Generic;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Services;

public static class AppSettingsValidator
{
    public const int MinimumHoverRevealWidth = 80;
    public const int MaximumHoverRevealWidth = 1400;
    public const int MinimumHoverRevealHeight = 20;
    public const int MaximumHoverRevealHeight = 420;
    public const double MinimumMaskIntensity = 0.60d;
    public const double MaximumMaskIntensity = 2.40d;
    public const double MinimumZoneStrength = 0.15d;
    public const double MaximumZoneStrength = 2.40d;

    public static IReadOnlyList<SettingsValidationError> Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<SettingsValidationError>();
        foreach (var profile in settings.AppProfiles)
        {
            ValidateProfile(profile, errors);
        }

        return errors;
    }

    private static void ValidateProfile(AppProfile profile, ICollection<SettingsValidationError> errors)
    {
        var profileName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.AppId.ToString()
            : profile.DisplayName.Trim();

        if (profile.HoverRevealWidthPixels is < MinimumHoverRevealWidth or > MaximumHoverRevealWidth)
        {
            errors.Add(new SettingsValidationError(
                $"{profileName} > Hover width",
                $"Enter a value from {MinimumHoverRevealWidth} to {MaximumHoverRevealWidth} pixels."));
        }

        if (profile.HoverRevealHeightPixels is < MinimumHoverRevealHeight or > MaximumHoverRevealHeight)
        {
            errors.Add(new SettingsValidationError(
                $"{profileName} > Hover height",
                $"Enter a value from {MinimumHoverRevealHeight} to {MaximumHoverRevealHeight} pixels."));
        }

        if (!double.IsFinite(profile.MaskIntensity)
            || profile.MaskIntensity is < MinimumMaskIntensity or > MaximumMaskIntensity)
        {
            errors.Add(new SettingsValidationError(
                $"{profileName} > Mask intensity",
                $"Choose a value from {MinimumMaskIntensity:0.00} to {MaximumMaskIntensity:0.00}."));
        }

        for (var index = 0; index < profile.Zones.Count; index++)
        {
            ValidateZone(profileName, profile.Zones[index], index, errors);
        }
    }

    private static void ValidateZone(
        string profileName,
        PrivacyZone zone,
        int index,
        ICollection<SettingsValidationError> errors)
    {
        var zoneName = string.IsNullOrWhiteSpace(zone.DisplayName)
            ? $"Zone {index + 1}"
            : zone.DisplayName.Trim();
        var location = $"{profileName} > {zoneName}";

        if (string.IsNullOrWhiteSpace(zone.DisplayName))
        {
            errors.Add(new SettingsValidationError(location, "Enter a zone name."));
        }

        ValidateUnitCoordinate(zone.RelativeRect.X, location, "X", errors);
        ValidateUnitCoordinate(zone.RelativeRect.Y, location, "Y", errors);
        ValidateUnitSize(zone.RelativeRect.Width, location, "Width", errors);
        ValidateUnitSize(zone.RelativeRect.Height, location, "Height", errors);

        if (AreFinite(zone.RelativeRect.X, zone.RelativeRect.Width)
            && zone.RelativeRect.X + zone.RelativeRect.Width > 1d)
        {
            errors.Add(new SettingsValidationError(
                location,
                "X plus Width must not extend beyond 1.00."));
        }

        if (AreFinite(zone.RelativeRect.Y, zone.RelativeRect.Height)
            && zone.RelativeRect.Y + zone.RelativeRect.Height > 1d)
        {
            errors.Add(new SettingsValidationError(
                location,
                "Y plus Height must not extend beyond 1.00."));
        }

        if (!double.IsFinite(zone.Strength)
            || zone.Strength is < MinimumZoneStrength or > MaximumZoneStrength)
        {
            errors.Add(new SettingsValidationError(
                location,
                $"Strength must be from {MinimumZoneStrength:0.00} to {MaximumZoneStrength:0.00}."));
        }
    }

    private static void ValidateUnitCoordinate(
        double value,
        string location,
        string fieldName,
        ICollection<SettingsValidationError> errors)
    {
        if (!double.IsFinite(value) || value is < 0d or > 1d)
        {
            errors.Add(new SettingsValidationError(location, $"{fieldName} must be from 0.00 to 1.00."));
        }
    }

    private static void ValidateUnitSize(
        double value,
        string location,
        string fieldName,
        ICollection<SettingsValidationError> errors)
    {
        if (!double.IsFinite(value) || value is <= 0d or > 1d)
        {
            errors.Add(new SettingsValidationError(location, $"{fieldName} must be greater than 0.00 and at most 1.00."));
        }
    }

    private static bool AreFinite(double first, double second)
    {
        return double.IsFinite(first) && double.IsFinite(second);
    }
}
