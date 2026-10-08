namespace PrivacyMask.Windows.Models;

public readonly record struct MaskSurfaceAppearance(
    double Intensity,
    double SmoothedIntensity,
    double OverlayOpacity,
    double HighlightBlend,
    double MidtoneBlend,
    double ShadowBlend,
    double TextureContrast,
    double TextureScale);
