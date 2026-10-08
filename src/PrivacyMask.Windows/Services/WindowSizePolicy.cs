using System;
using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public static class WindowSizePolicy
{
    public static WindowSizeConstraints FitToWorkArea(
        double workAreaWidth,
        double workAreaHeight,
        double desiredWidth,
        double desiredHeight,
        double configuredMinWidth,
        double configuredMinHeight,
        double outerMargin = 32d)
    {
        ValidatePositive(workAreaWidth, nameof(workAreaWidth));
        ValidatePositive(workAreaHeight, nameof(workAreaHeight));
        ValidatePositive(desiredWidth, nameof(desiredWidth));
        ValidatePositive(desiredHeight, nameof(desiredHeight));
        ValidatePositive(configuredMinWidth, nameof(configuredMinWidth));
        ValidatePositive(configuredMinHeight, nameof(configuredMinHeight));

        if (!double.IsFinite(outerMargin) || outerMargin < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(outerMargin));
        }

        var availableWidth = Math.Max(1d, workAreaWidth - outerMargin);
        var availableHeight = Math.Max(1d, workAreaHeight - outerMargin);
        var width = Math.Min(desiredWidth, availableWidth);
        var height = Math.Min(desiredHeight, availableHeight);

        return new WindowSizeConstraints(
            width,
            height,
            Math.Min(configuredMinWidth, width),
            Math.Min(configuredMinHeight, height));
    }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
