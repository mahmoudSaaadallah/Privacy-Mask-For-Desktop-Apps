using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public static class FrostedSurfaceRasterizer
{
    public const int TileSize = 192;

    public static FrostedSurfaceTile Render(
        RgbColor surface,
        RgbColor highlight,
        RgbColor shadow,
        double textureContrast)
    {
        var contrast = double.Clamp(textureContrast, 0d, 1d);
        var stride = TileSize * 4;
        var pixels = GC.AllocateUninitializedArray<byte>(stride * TileSize);

        for (var y = 0; y < TileSize; y++)
        {
            for (var x = 0; x < TileSize; x++)
            {
                var highlightCloud = RadialCloud(x, y, 46d, 48d, 82d, 68d);
                var shadowCloud = RadialCloud(x, y, 158d, 142d, 90d, 78d);
                var edgeHighlight = RadialCloud(x, y, 176d, 20d, 54d, 42d);

                var red = (double)surface.Red;
                var green = (double)surface.Green;
                var blue = (double)surface.Blue;

                BlendInto(ref red, ref green, ref blue, highlight, highlightCloud * (0.10d + (contrast * 0.32d)));
                BlendInto(ref red, ref green, ref blue, shadow, shadowCloud * (0.08d + (contrast * 0.26d)));
                BlendInto(ref red, ref green, ref blue, highlight, edgeHighlight * (0.06d + (contrast * 0.18d)));

                var grain = (UnitNoise(x, y) - 0.5d) * contrast * 24d;
                var offset = (y * stride) + (x * 4);
                pixels[offset] = ToByte(blue + grain);
                pixels[offset + 1] = ToByte(green + grain);
                pixels[offset + 2] = ToByte(red + grain);
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return new FrostedSurfaceTile(TileSize, TileSize, stride, pixels);
    }

    private static double RadialCloud(
        double x,
        double y,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY)
    {
        var normalizedX = (x - centerX) / radiusX;
        var normalizedY = (y - centerY) / radiusY;
        var progress = double.Clamp(1d - ((normalizedX * normalizedX) + (normalizedY * normalizedY)), 0d, 1d);
        return progress * progress * (3d - (2d * progress));
    }

    private static double UnitNoise(int x, int y)
    {
        var value = unchecked(((uint)x * 0x1F123BB5u) ^ ((uint)y * 0x5F356495u) ^ 0x9E3779B9u);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        return (value & 0xFFFFu) / 65535d;
    }

    private static void BlendInto(
        ref double red,
        ref double green,
        ref double blue,
        RgbColor target,
        double amount)
    {
        red += (target.Red - red) * amount;
        green += (target.Green - green) * amount;
        blue += (target.Blue - blue) * amount;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(double.Clamp(value, byte.MinValue, byte.MaxValue));
    }
}
