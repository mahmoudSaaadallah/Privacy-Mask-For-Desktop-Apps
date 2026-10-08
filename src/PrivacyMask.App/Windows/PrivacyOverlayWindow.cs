using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Interop;
using PrivacyMask.Windows.Models;
using PrivacyMask.Windows.Services;
using Brush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace PrivacyMask.App.Windows;

public sealed class PrivacyOverlayWindow : Window
{
    private readonly Canvas _canvas;
    private ScreenRect[] _lastOccludingBounds = [];
    private AppProfile? _lastProfile;
    private IReadOnlyList<PrivacyZone>? _lastEffectiveZones;
    private RuntimeMode _lastMode;
    private ScreenRect _lastBounds;
    private MaskColorOption _lastMaskColor;
    private double _lastMaskIntensity;
    private int _lastHoverRevealWidthPixels;
    private int _lastHoverRevealHeightPixels;
    private bool _lastTemporaryRevealHeld;
    private bool _lastCursorAffectsRender;
    private Point _lastCursorScreenPoint;
    private bool _hasRenderState;

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
        var renderPolicy = ProtectionRenderPolicy.ForMode(mode);
        if (!renderPolicy.ShouldRender || trackedWindow.Snapshot.IsMinimized || trackedWindow.Snapshot.Bounds.IsEmpty)
        {
            HideOverlay();
            return;
        }

        var wasVisible = IsVisible;
        if (!wasVisible)
        {
            Show();
        }

        var cursorAffectsRender = DoesCursorAffectRender(
            trackedWindow,
            renderPolicy,
            temporaryRevealHeld,
            cursorScreenPoint);
        if (wasVisible && IsRenderStateCurrent(
                trackedWindow,
                mode,
                temporaryRevealHeld,
                cursorAffectsRender,
                cursorScreenPoint))
        {
            return;
        }

        ApplyWindowBounds(trackedWindow.Snapshot.Bounds);
        RenderZones(trackedWindow, renderPolicy, temporaryRevealHeld, cursorScreenPoint);
        CaptureRenderState(
            trackedWindow,
            mode,
            temporaryRevealHeld,
            cursorAffectsRender,
            cursorScreenPoint);
    }

    public void HideOverlay()
    {
        if (IsVisible)
        {
            Hide();
        }

        _canvas.Children.Clear();
        _hasRenderState = false;
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

    private void RenderZones(
        TrackedWindow trackedWindow,
        ProtectionRenderPolicy renderPolicy,
        bool temporaryRevealHeld,
        Point cursorScreenPoint)
    {
        _canvas.Children.Clear();
        var width = ActualWidth <= 0 ? Width : ActualWidth;
        var height = ActualHeight <= 0 ? Height : ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (renderPolicy.ForceFullWindowMask)
        {
            var fullWindowRect = new Rect(0d, 0d, width, height);
            var occludingCutouts = CreateOcclusionCutouts(fullWindowRect, trackedWindow.OccludingBounds);
            AddMaskedRegion(
                fullWindowRect,
                renderPolicy.ForcedStyle,
                renderPolicy.ForcedColor,
                renderPolicy.ForcedStrength,
                revealCutout: null,
                occludingCutouts: occludingCutouts,
                cornerRadius: 0d);
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

            var revealCutout = renderPolicy.AllowHoverReveal && zone.Behavior.HasFlag(ZoneBehavior.RevealOnHover)
                ? CreateHoverRevealCutout(
                    zoneRect,
                    cursorScreenPoint,
                    trackedWindow.Profile.HoverRevealWidthPixels,
                    trackedWindow.Profile.HoverRevealHeightPixels)
                : null;

            var occludingCutouts = CreateOcclusionCutouts(zoneRect, trackedWindow.OccludingBounds);
            AddMaskedRegion(zoneRect, zone.Style, trackedWindow.Profile.MaskColor, zone.Strength, revealCutout, occludingCutouts);
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

    private static bool DoesCursorAffectRender(
        TrackedWindow trackedWindow,
        ProtectionRenderPolicy renderPolicy,
        bool temporaryRevealHeld,
        Point cursorScreenPoint)
    {
        if (!renderPolicy.AllowHoverReveal)
        {
            return false;
        }

        foreach (var zone in trackedWindow.EffectiveZones)
        {
            if (!zone.Enabled
                || !zone.Behavior.HasFlag(ZoneBehavior.RevealOnHover)
                || temporaryRevealHeld && zone.Behavior.HasFlag(ZoneBehavior.HideDuringTemporaryReveal))
            {
                continue;
            }

            if (IsCursorInsideZone(cursorScreenPoint, trackedWindow.Snapshot.Bounds, zone.RelativeRect))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsRenderStateCurrent(
        TrackedWindow trackedWindow,
        RuntimeMode mode,
        bool temporaryRevealHeld,
        bool cursorAffectsRender,
        Point cursorScreenPoint)
    {
        if (!_hasRenderState
            || _lastMode != mode
            || _lastBounds != trackedWindow.Snapshot.Bounds
            || !ReferenceEquals(_lastProfile, trackedWindow.Profile)
            || !ReferenceEquals(_lastEffectiveZones, trackedWindow.EffectiveZones)
            || _lastMaskColor != trackedWindow.Profile.MaskColor
            || _lastMaskIntensity != trackedWindow.Profile.MaskIntensity
            || _lastHoverRevealWidthPixels != trackedWindow.Profile.HoverRevealWidthPixels
            || _lastHoverRevealHeightPixels != trackedWindow.Profile.HoverRevealHeightPixels
            || _lastTemporaryRevealHeld != temporaryRevealHeld
            || _lastCursorAffectsRender != cursorAffectsRender
            || cursorAffectsRender && _lastCursorScreenPoint != cursorScreenPoint
            || !OccludingBoundsEqual(trackedWindow.OccludingBounds))
        {
            return false;
        }

        return true;
    }

    private bool OccludingBoundsEqual(IReadOnlyList<ScreenRect> occludingBounds)
    {
        if (_lastOccludingBounds.Length != occludingBounds.Count)
        {
            return false;
        }

        for (var index = 0; index < occludingBounds.Count; index++)
        {
            if (_lastOccludingBounds[index] != occludingBounds[index])
            {
                return false;
            }
        }

        return true;
    }

    private void CaptureRenderState(
        TrackedWindow trackedWindow,
        RuntimeMode mode,
        bool temporaryRevealHeld,
        bool cursorAffectsRender,
        Point cursorScreenPoint)
    {
        _lastMode = mode;
        _lastBounds = trackedWindow.Snapshot.Bounds;
        _lastProfile = trackedWindow.Profile;
        _lastEffectiveZones = trackedWindow.EffectiveZones;
        _lastMaskColor = trackedWindow.Profile.MaskColor;
        _lastMaskIntensity = trackedWindow.Profile.MaskIntensity;
        _lastHoverRevealWidthPixels = trackedWindow.Profile.HoverRevealWidthPixels;
        _lastHoverRevealHeightPixels = trackedWindow.Profile.HoverRevealHeightPixels;
        _lastTemporaryRevealHeld = temporaryRevealHeld;
        _lastCursorAffectsRender = cursorAffectsRender;
        _lastCursorScreenPoint = cursorScreenPoint;

        if (_lastOccludingBounds.Length != trackedWindow.OccludingBounds.Count)
        {
            _lastOccludingBounds = new ScreenRect[trackedWindow.OccludingBounds.Count];
        }

        for (var index = 0; index < trackedWindow.OccludingBounds.Count; index++)
        {
            _lastOccludingBounds[index] = trackedWindow.OccludingBounds[index];
        }

        _hasRenderState = true;
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

    private IReadOnlyList<Rect> CreateOcclusionCutouts(Rect zoneRect, IReadOnlyList<ScreenRect> occludingBounds)
    {
        if (occludingBounds.Count == 0)
        {
            return [];
        }

        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cutouts = new List<Rect>();

        foreach (var occludingBoundsRect in occludingBounds)
        {
            var topLeft = fromDevice.Transform(new Point(occludingBoundsRect.Left, occludingBoundsRect.Top));
            var bottomRight = fromDevice.Transform(new Point(occludingBoundsRect.Right, occludingBoundsRect.Bottom));
            var localRect = new Rect(
                topLeft.X - Left,
                topLeft.Y - Top,
                bottomRight.X - topLeft.X,
                bottomRight.Y - topLeft.Y);

            var intersected = Rect.Intersect(zoneRect, localRect);
            if (intersected.Width > 0 && intersected.Height > 0)
            {
                cutouts.Add(intersected);
            }
        }

        return cutouts;
    }

    private static Brush BuildBackground(
        MaskStyle style,
        MaskColorOption maskColor,
        MaskSurfaceAppearance appearance)
    {
        var baseColor = GetMaskBaseColor(maskColor);
        var brush = style switch
        {
            MaskStyle.Pixelate => CreatePixelBrush(baseColor, appearance),
            MaskStyle.SolidRedact => CreateSolidRedactBrush(baseColor, appearance),
            _ => CreateFrostedGlassBrush(baseColor, appearance),
        };

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private static Brush CreateFrostedGlassBrush(
        MediaColor baseColor,
        MaskSurfaceAppearance appearance)
    {
        var neutralFrost = MediaColor.FromRgb(216, 222, 225);
        var surface = Blend(baseColor, neutralFrost, appearance.MidtoneBlend);
        var highlight = Blend(baseColor, MediaColor.FromRgb(255, 255, 255), appearance.HighlightBlend);
        var shadow = Blend(baseColor, MediaColor.FromRgb(0, 0, 0), appearance.ShadowBlend);
        var tile = FrostedSurfaceRasterizer.Render(
            ToRgbColor(surface),
            ToRgbColor(highlight),
            ToRgbColor(shadow),
            appearance.TintOpacity);
        var bitmap = BitmapSource.Create(
            tile.Width,
            tile.Height,
            96d,
            96d,
            PixelFormats.Bgra32,
            null,
            tile.BgraPixels,
            tile.Stride);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0d, 0d, 1d, 1d),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(
                0d,
                0d,
                tile.Width * appearance.PixelScale,
                tile.Height * appearance.PixelScale),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        return brush;
    }

    private static Brush CreatePixelBrush(
        MediaColor baseColor,
        MaskSurfaceAppearance appearance)
    {
        var neutralFrost = MediaColor.FromRgb(216, 222, 225);
        var dark = Blend(baseColor, MediaColor.FromRgb(0, 0, 0), appearance.ShadowBlend);
        var light = Blend(baseColor, neutralFrost, appearance.HighlightBlend);
        var tileSize = 16d * appearance.PixelScale;
        var halfTileSize = tileSize / 2d;

        var drawingBrush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tileSize, tileSize),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, tileSize, tileSize),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(light), null, new RectangleGeometry(new Rect(0, 0, tileSize, tileSize))));
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(0, 0, halfTileSize, halfTileSize))));
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(halfTileSize, halfTileSize, halfTileSize, halfTileSize))));
        drawingBrush.Drawing = drawingGroup;
        return drawingBrush;
    }

    private static Brush CreateSolidRedactBrush(
        MediaColor baseColor,
        MaskSurfaceAppearance appearance)
    {
        var toned = Blend(baseColor, MediaColor.FromRgb(0, 0, 0), appearance.ShadowBlend);
        return new SolidColorBrush(toned);
    }

    private void AddMaskedRegion(
        Rect zoneRect,
        MaskStyle style,
        MaskColorOption maskColor,
        double strength,
        Rect? revealCutout,
        IReadOnlyList<Rect> occludingCutouts,
        double cornerRadius = 14d)
    {
        var appearance = MaskAppearancePolicy.Resolve(style, strength);
        var fill = BuildBackground(style, maskColor, appearance);
        Geometry geometry = new RectangleGeometry(zoneRect, cornerRadius, cornerRadius);

        if (revealCutout is not null)
        {
            geometry = new CombinedGeometry(GeometryCombineMode.Exclude, geometry, new RectangleGeometry(revealCutout.Value));
        }

        foreach (var cutout in occludingCutouts)
        {
            geometry = new CombinedGeometry(GeometryCombineMode.Exclude, geometry, new RectangleGeometry(cutout));
        }

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        var shape = new Path
        {
            Data = geometry,
            Fill = fill,
            Opacity = appearance.OverlayOpacity,
            IsHitTestVisible = false,
        };

        _canvas.Children.Add(shape);
    }

    private static RgbColor ToRgbColor(MediaColor color)
    {
        return new RgbColor(color.R, color.G, color.B);
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
