namespace PrivacyMask.Windows.Models;

public readonly record struct WindowSizeConstraints(
    double Width,
    double Height,
    double MinWidth,
    double MinHeight);
