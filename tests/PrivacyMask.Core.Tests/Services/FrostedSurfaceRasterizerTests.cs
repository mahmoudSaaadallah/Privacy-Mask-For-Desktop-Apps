using PrivacyMask.Windows.Models;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class FrostedSurfaceRasterizerTests
{
    private static readonly RgbColor Surface = new(160, 168, 172);
    private static readonly RgbColor Highlight = new(232, 236, 238);
    private static readonly RgbColor Shadow = new(64, 70, 74);

    [Fact]
    public void Render_ProducesAFullyOpaqueTile()
    {
        var tile = FrostedSurfaceRasterizer.Render(Surface, Highlight, Shadow, 0.12d);

        Assert.Equal(FrostedSurfaceRasterizer.TileSize, tile.Width);
        Assert.Equal(FrostedSurfaceRasterizer.TileSize, tile.Height);
        Assert.Equal(tile.Width * 4, tile.Stride);
        Assert.Equal(tile.Stride * tile.Height, tile.BgraPixels.Length);
        Assert.All(
            Enumerable.Range(0, tile.Width * tile.Height),
            pixelIndex => Assert.Equal(byte.MaxValue, tile.BgraPixels[(pixelIndex * 4) + 3]));
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        var first = FrostedSurfaceRasterizer.Render(Surface, Highlight, Shadow, 0.12d);
        var second = FrostedSurfaceRasterizer.Render(Surface, Highlight, Shadow, 0.12d);

        Assert.Equal(first.BgraPixels, second.BgraPixels);
    }

    [Fact]
    public void Render_ChangesTextureWhenContrastChanges()
    {
        var soft = FrostedSurfaceRasterizer.Render(Surface, Highlight, Shadow, 0.04d);
        var strong = FrostedSurfaceRasterizer.Render(Surface, Highlight, Shadow, 0.15d);

        Assert.NotEqual(soft.BgraPixels, strong.BgraPixels);
    }
}
