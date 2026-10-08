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
    public const double MinimumMaskIntensity = MaskIntensityScale.Minimum;
    public const double MaximumMaskIntensity = MaskIntensityScale.Maximum;
    public const double MinimumZoneStrength = MaskIntensityScale.Minimum;
    public const double MaximumZoneStrength = MaskIntensityScale.Maximum;

    public static IReadOnlyList<SettingsValidationError> Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<SettingsValidationError>();
        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processOwners = new Dictionary<string, AppProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in settings.AppProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                errors.Add(new SettingsValidationError("App profiles", "Every app profile must have a stable identifier."));
            }
            else if (!profileIds.Add(profile.ProfileId.Trim()))
            {
                errors.Add(new SettingsValidationError(profile.DisplayName, "This app profile identifier is already in use."));
            }

            foreach (var processName in profile.WindowMatchers
                .SelectMany(matcher => matcher.ProcessNames)
                .Where(processName => !string.IsNullOrWhiteSpace(processName))
                .Select(processName => processName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (processOwners.TryGetValue(processName, out var existingOwner)
                    && !ReferenceEquals(existingOwner, profile))
                {
                    errors.Add(new SettingsValidationError(
                        profile.DisplayName,
                        $"{processName}.exe is already protected by {existingOwner.DisplayName}."));
                }
                else
                {
                    processOwners[processName] = profile;
                }
            }

            ValidateProfile(profile, errors);
        }

        return errors;
    }

    private static void ValidateProfile(AppProfile profile, ICollection<SettingsValidationError> errors)
    {
        var profileName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.AppId.ToString()
            : profile.DisplayName.Trim();

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            errors.Add(new SettingsValidationError(profileName, "Enter an app profile name."));
        }

        if (profile.AppId == AppId.Custom
            && !profile.WindowMatchers.Any(matcher =>
                matcher.ProcessNames.Any(processName => !string.IsNullOrWhiteSpace(processName))))
        {
            errors.Add(new SettingsValidationError(profileName, "Choose at least one application process to protect."));
        }

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
                "Choose a blur strength from 0% to 100%."));
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
                $"Blur strength must be from {MinimumZoneStrength:P0} to {MaximumZoneStrength:P0}."));
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
