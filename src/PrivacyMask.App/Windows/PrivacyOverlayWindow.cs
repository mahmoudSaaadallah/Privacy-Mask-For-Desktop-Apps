using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Interop;
using Brush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;
using ShapeRectangle = System.Windows.Shapes.Rectangle;

namespace PrivacyMask.App.Windows;

public sealed class PrivacyOverlayWindow : Window
{
    private readonly Canvas _canvas;

    public PrivacyOverlayWindow()
    {
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;

        _canvas = new Canvas
        {
            Background = MediaBrushes.Transparent,
            IsHitTestVisible = false,
        };

        Content = _canvas;
        Loaded += OnLoaded;
    }

    public void UpdateOverlay(TrackedWindow trackedWindow, RuntimeMode mode, bool temporaryRevealHeld, Point cursorScreenPoint)
    {
        if (mode is RuntimeMode.Off or RuntimeMode.Panic || trackedWindow.Snapshot.IsMinimized || trackedWindow.Snapshot.Bounds.IsEmpty)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        ApplyWindowBounds(trackedWindow.Snapshot.Bounds);
        RenderZones(trackedWindow, temporaryRevealHeld, cursorScreenPoint);
    }

    public void HideOverlay()
    {
        if (IsVisible)
        {
            Hide();
        }

        _canvas.Children.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            var extendedStyles = NativeMethods.GetWindowLongPtr(source.Handle, NativeMethods.GwlExStyle).ToInt64();
            extendedStyles |= NativeMethods.WsExLayered | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
            NativeMethods.SetWindowLongPtr(source.Handle, NativeMethods.GwlExStyle, new nint(extendedStyles));
        }
    }

    private void ApplyWindowBounds(ScreenRect bounds)
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(bounds.Left, bounds.Top));
        var bottomRight = transform.Transform(new Point(bounds.Right, bounds.Bottom));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;

        if (source is HwndSource hwndSource)
        {
            NativeMethods.SetWindowPos(
                hwndSource.Handle,
                NativeMethods.HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);
        }
    }

    private void RenderZones(TrackedWindow trackedWindow, bool temporaryRevealHeld, Point cursorScreenPoint)
    {
        _canvas.Children.Clear();
        var width = ActualWidth <= 0 ? Width : ActualWidth;
        var height = ActualHeight <= 0 ? Height : ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        foreach (var zone in trackedWindow.EffectiveZones.Where(zone => zone.Enabled))
        {
            var isTemporaryReveal = temporaryRevealHeld && zone.Behavior.HasFlag(ZoneBehavior.HideDuringTemporaryReveal);

            if (isTemporaryReveal)
            {
                continue;
            }

            var zoneRect = new Rect(
                zone.RelativeRect.X * width,
                zone.RelativeRect.Y * height,
                zone.RelativeRect.Width * width,
                zone.RelativeRect.Height * height);

            if (zoneRect.Width <= 0 || zoneRect.Height <= 0)
            {
                continue;
            }

            var revealCutout = zone.Behavior.HasFlag(ZoneBehavior.RevealOnHover)
                ? CreateHoverRevealCutout(
                    zoneRect,
                    cursorScreenPoint,
                    trackedWindow.Profile.HoverRevealWidthPixels,
                    trackedWindow.Profile.HoverRevealHeightPixels)
                : null;

            AddMaskedRegion(zoneRect, zone.Style, trackedWindow.Profile.MaskColor, zone.Strength, revealCutout);
        }
    }

    private static bool IsCursorInsideZone(Point cursorScreenPoint, ScreenRect bounds, RelativeRect relativeRect)
    {
        var left = bounds.Left + (relativeRect.X * bounds.Width);
        var top = bounds.Top + (relativeRect.Y * bounds.Height);
        var width = relativeRect.Width * bounds.Width;
        var height = relativeRect.Height * bounds.Height;

        return cursorScreenPoint.X >= left
            && cursorScreenPoint.X <= left + width
            && cursorScreenPoint.Y >= top
            && cursorScreenPoint.Y <= top + height;
    }

    private Rect? CreateHoverRevealCutout(Rect zoneRect, Point cursorScreenPoint, double hoverRevealWidthPixels, double hoverRevealHeightPixels)
    {
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cursorDip = fromDevice.Transform(cursorScreenPoint);
        var localCursor = new Point(cursorDip.X - Left, cursorDip.Y - Top);

        if (!zoneRect.Contains(localCursor))
        {
            return null;
        }

        var revealSize = fromDevice.Transform(new Point(hoverRevealWidthPixels, hoverRevealHeightPixels));
        var revealWidth = Math.Abs(revealSize.X);
        var revealHeight = Math.Abs(revealSize.Y);
        var requestedRect = new Rect(
            localCursor.X - (revealWidth / 2d),
            localCursor.Y - (revealHeight / 2d),
            revealWidth,
            revealHeight);

        var intersected = Rect.Intersect(zoneRect, requestedRect);
        return intersected.Width > 0 && intersected.Height > 0 ? intersected : null;
    }

    private static Brush BuildBackground(MaskStyle style, MaskColorOption maskColor, double strength)
    {
        var normalized = NormalizeStrength(strength);
        var baseColor = GetMaskBaseColor(maskColor);
        if (normalized >= 0.999d)
        {
            return new SolidColorBrush(baseColor);
        }

        return style switch
        {
            MaskStyle.Pixelate => CreatePixelBrush(baseColor, normalized),
            MaskStyle.SolidRedact => CreateSolidRedactBrush(baseColor, normalized),
            _ => CreateBlurBrush(baseColor, normalized),
        };
    }

    private static Brush CreateBlurBrush(MediaColor baseColor, double normalized)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Blend(baseColor, MediaColor.FromRgb(255, 255, 255), 0.62d - (0.20d * normalized)), 0.0));
        brush.GradientStops.Add(new GradientStop(Blend(baseColor, MediaColor.FromRgb(0, 0, 0), 0.22d + (0.12d * normalized)), 0.45));
        brush.GradientStops.Add(new GradientStop(Blend(baseColor, MediaColor.FromRgb(0, 0, 0), 0.42d + (0.24d * normalized)), 1.0));
        return brush;
    }

    private static Brush CreatePixelBrush(MediaColor baseColor, double normalized)
    {
        var dark = Blend(baseColor, MediaColor.FromRgb(0, 0, 0), 0.34d + (0.24d * normalized));
        var light = Blend(baseColor, MediaColor.FromRgb(255, 255, 255), 0.52d - (0.18d * normalized));

        var drawingBrush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 16, 16),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(light), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        drawingBrush.Drawing = drawingGroup;
        return drawingBrush;
    }

    private static Brush CreateSolidRedactBrush(MediaColor baseColor, double normalized)
    {
        var toned = Blend(baseColor, MediaColor.FromRgb(0, 0, 0), 0.12d + (0.10d * normalized));
        return new SolidColorBrush(toned);
    }

    private static double ComputeOverlayOpacity(MaskStyle style, double strength)
    {
        var normalized = NormalizeStrength(strength);
        if (normalized >= 0.999d)
        {
            return 1.0d;
        }

        return style switch
        {
            MaskStyle.SolidRedact => 0.86d + (0.10d * normalized),
            _ => 0.72d + (0.22d * normalized),
        };
    }

    private void AddMaskedRegion(
        Rect zoneRect,
        MaskStyle style,
        MaskColorOption maskColor,
        double strength,
        Rect? revealCutout)
    {
        var fill = BuildBackground(style, maskColor, strength);
        var opacity = ComputeOverlayOpacity(style, strength);

        if (revealCutout is null)
        {
            AddRectangle(zoneRect, fill, opacity);
            return;
        }

        var hole = revealCutout.Value;

        AddRectangle(new Rect(zoneRect.Left, zoneRect.Top, zoneRect.Width, hole.Top - zoneRect.Top), fill, opacity);
        AddRectangle(new Rect(zoneRect.Left, hole.Bottom, zoneRect.Width, zoneRect.Bottom - hole.Bottom), fill, opacity);
        AddRectangle(new Rect(zoneRect.Left, hole.Top, hole.Left - zoneRect.Left, hole.Height), fill, opacity);
        AddRectangle(new Rect(hole.Right, hole.Top, zoneRect.Right - hole.Right, hole.Height), fill, opacity);
    }

    private void AddRectangle(Rect rect, Brush fill, double opacity)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var shape = new ShapeRectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = fill,
            Opacity = opacity,
            RadiusX = 14,
            RadiusY = 14,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(shape, rect.X);
        Canvas.SetTop(shape, rect.Y);
        _canvas.Children.Add(shape);
    }

    private static double NormalizeStrength(double strength)
    {
        return double.Clamp((strength - 0.15d) / 2.25d, 0d, 1d);
    }

    private static MediaColor GetMaskBaseColor(MaskColorOption maskColor)
    {
        return maskColor switch
        {
            MaskColorOption.Red => MediaColor.FromRgb(201, 52, 52),
            MaskColorOption.Green => MediaColor.FromRgb(35, 129, 74),
            MaskColorOption.Blue => MediaColor.FromRgb(43, 99, 204),
            MaskColorOption.Gray => MediaColor.FromRgb(98, 104, 112),
            MaskColorOption.White => MediaColor.FromRgb(250, 249, 245),
            _ => MediaColor.FromRgb(0, 0, 0),
        };
    }

    private static MediaColor Blend(MediaColor source, MediaColor target, double amount)
    {
        var normalized = double.Clamp(amount, 0d, 1d);
        return MediaColor.FromRgb(
            (byte)Math.Round(source.R + ((target.R - source.R) * normalized)),
            (byte)Math.Round(source.G + ((target.G - source.G) * normalized)),
            (byte)Math.Round(source.B + ((target.B - source.B) * normalized)));
    }
}
