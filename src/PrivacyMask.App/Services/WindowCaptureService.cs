using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.App.Services;

internal static class WindowCaptureService
{
    private const uint PrintWindowRenderFullContent = 0x00000002;

    public static BitmapSource? TryCapture(nint windowHandle, ScreenRect bounds, double downsampleFactor)
    {
        if (windowHandle == nint.Zero || bounds.IsEmpty)
        {
            return null;
        }

        var outputSize = BlurCaptureSizing.Calculate(bounds.Width, bounds.Height, downsampleFactor);
        if (outputSize.Width <= 0 || outputSize.Height <= 0)
        {
            return null;
        }

        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            var deviceContext = graphics.GetHdc();
            try
            {
                if (!PrintWindow(windowHandle, deviceContext, PrintWindowRenderFullContent))
                {
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }

        if (outputSize.Width == source.Width && outputSize.Height == source.Height)
        {
            return CreateBitmapSource(source);
        }

        using var sampled = new Bitmap(outputSize.Width, outputSize.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(sampled))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, sampled.Width, sampled.Height),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel);
        }

        return CreateBitmapSource(sampled);
    }

    private static BitmapSource CreateBitmapSource(Bitmap bitmap)
    {
        var bitmapHandle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(bitmapHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint windowHandle, nint deviceContext, uint flags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);
}
