namespace PrivacyMask.Windows.Models;

public sealed record FrostedSurfaceTile(int Width, int Height, int Stride, byte[] BgraPixels);
