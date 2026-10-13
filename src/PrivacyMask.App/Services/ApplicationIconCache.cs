using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrivacyMask.App.Services;

public static class ApplicationIconCache
{
    private const int Capacity = 64;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ImageSource> Images = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Keys = new();
    private static ImageSource? _fallback;

    public static ImageSource Get(string? executablePath)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return Fallback();
            try
            {
                var file = new FileInfo(executablePath);
                if (!file.Exists) return Fallback();
                var key = $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}";
                if (Images.TryGetValue(key, out var existing)) return existing;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(file.FullName);
                if (icon is null) return Fallback();
                var image = FromIcon(icon);
                if (Images.Count == Capacity) Images.Remove(Keys.Dequeue());
                Images.Add(key, image);
                Keys.Enqueue(key);
                return image;
            }
            catch
            {
                return Fallback();
            }
        }
    }

    private static ImageSource Fallback()
    {
        if (_fallback is not null) return _fallback;
        using var icon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        return _fallback = FromIcon(icon);
    }

    private static ImageSource FromIcon(System.Drawing.Icon icon)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        image.Freeze();
        return image;
    }
}
