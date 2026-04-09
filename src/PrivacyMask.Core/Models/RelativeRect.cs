using System;
using System.Text.Json.Serialization;

namespace PrivacyMask.Core.Models;

public sealed record RelativeRect
{
    public RelativeRect()
    {
    }

    [JsonConstructor]
    public RelativeRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public RelativeRect Clamp()
    {
        var clampedX = Math.Clamp(X, 0d, 1d);
        var clampedY = Math.Clamp(Y, 0d, 1d);
        var clampedWidth = Math.Clamp(Width, 0d, 1d - clampedX);
        var clampedHeight = Math.Clamp(Height, 0d, 1d - clampedY);

        return new RelativeRect(clampedX, clampedY, clampedWidth, clampedHeight);
    }
}
