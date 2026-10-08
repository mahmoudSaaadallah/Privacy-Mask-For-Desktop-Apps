using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Interop;
using PrivacyMask.Windows.Models;
using PrivacyMask.Windows.Services;
using Brush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;
using WpfImage = System.Windows.Controls.Image;

namespace PrivacyMask.App.Windows;

public sealed class PrivacyOverlayWindow : Window
{
    private static readonly TimeSpan BlurCaptureInterval = TimeSpan.FromMilliseconds(250d);
    private static readonly TimeSpan WetGlassCaptureInterval = TimeSpan.FromMilliseconds(500d);
    private static readonly Brush WetGlassTextureBrush = CreateWetGlassTextureBrush();

    private readonly Canvas _canvas;
    private readonly List<WpfImage> _blurImages = [];
    private readonly BlurCaptureLifecycle _blurCaptureLifecycle = new();
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
    private BitmapSource? _blurFrame;
    private BlurCaptureRequest? _captureRequest;
    private DateTime _nextCaptureUtc;
    private bool _isClosed;

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
        Closed += OnClosed;
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
        ScheduleBlurCapture(trackedWindow, renderPolicy, temporaryRevealHeld);
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
        _blurImages.Clear();
        ResetBlurCapture();
        _hasRenderState = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        ResetBlurCapture();
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
        _blurImages.Clear();
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
                overlayWidth: width,
                overlayHeight: height,
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
            AddMaskedRegion(
                zoneRect,
                zone.Style,
                trackedWindow.Profile.MaskColor,
                zone.Strength,
                revealCutout,
                occludingCutouts,
                width,
                height);
        }
    }

    private void ScheduleBlurCapture(
        TrackedWindow trackedWindow,
        ProtectionRenderPolicy renderPolicy,
        bool temporaryRevealHeld)
    {
        if (renderPolicy.ForceFullWindowMask
            || IsFullyOccluded(trackedWindow.Snapshot.Bounds, trackedWindow.OccludingBounds)
            || !TryGetLiveCaptureAppearance(
                trackedWindow,
                temporaryRevealHeld,
                out var captureStyle,
                out var appearance))
        {
            ResetBlurCapture();
            return;
        }

        var request = new BlurCaptureRequest(
            trackedWindow.Snapshot.Handle,
            trackedWindow.Snapshot.Bounds,
            appearance.BlurDownsampleFactor,
            captureStyle);
        if (_captureRequest != request)
        {
            var canReuseFrame = _captureRequest is { } previousRequest
                && CanReuseBlurFrame(previousRequest, request);
            _captureRequest = request;
            if (!canReuseFrame)
            {
                _blurFrame = null;
                _blurImages.Clear();
            }

            _hasRenderState = false;
            _blurCaptureLifecycle.Invalidate();
            _nextCaptureUtc = DateTime.MinValue;
        }

        var now = DateTime.UtcNow;
        if (now < _nextCaptureUtc || !_blurCaptureLifecycle.TryBegin(out var generation))
        {
            return;
        }

        _nextCaptureUtc = now + (captureStyle == MaskStyle.WetGlass
            ? WetGlassCaptureInterval
            : BlurCaptureInterval);
        _ = CaptureBlurFrameAsync(request, generation);
    }

    private static bool CanReuseBlurFrame(BlurCaptureRequest current, BlurCaptureRequest next)
    {
        return current.WindowHandle == next.WindowHandle
            && current.Bounds == next.Bounds
            && current.Style == next.Style;
    }

    private static bool TryGetLiveCaptureAppearance(
        TrackedWindow trackedWindow,
        bool temporaryRevealHeld,
        out MaskStyle captureStyle,
        out MaskSurfaceAppearance appearance)
    {
        foreach (var zone in trackedWindow.EffectiveZones)
        {
            if (zone.Enabled
                && UsesLiveWindowCapture(zone.Style)
                && !(temporaryRevealHeld && zone.Behavior.HasFlag(ZoneBehavior.HideDuringTemporaryReveal)))
            {
                captureStyle = zone.Style;
                appearance = MaskAppearancePolicy.Resolve(zone.Style, zone.Strength);
                return true;
            }
        }

        captureStyle = default;
        appearance = default;
        return false;
    }

    private static bool UsesLiveWindowCapture(MaskStyle style)
    {
        return style is MaskStyle.Blur or MaskStyle.WetGlass;
    }

    private static bool IsFullyOccluded(ScreenRect targetBounds, IReadOnlyList<ScreenRect> occludingBounds)
    {
        return occludingBounds.Any(bounds =>
            bounds.Left <= targetBounds.Left
            && bounds.Top <= targetBounds.Top
            && bounds.Right >= targetBounds.Right
            && bounds.Bottom >= targetBounds.Bottom);
    }

    private async Task CaptureBlurFrameAsync(BlurCaptureRequest request, int generation)
    {
        BitmapSource? frame = null;
        try
        {
            frame = await Task.Run(() => WindowCaptureService.TryCapture(
                request.WindowHandle,
                request.Bounds,
                request.DownsampleFactor));
        }
        catch
        {
            // A protected fallback remains visible when a window rejects capture.
        }

        var completion = _blurCaptureLifecycle.Complete(generation);
        if (_isClosed || completion == BlurCaptureCompletion.Ignored)
        {
            return;
        }

        if (completion == BlurCaptureCompletion.Stale || _captureRequest != request)
        {
            _nextCaptureUtc = DateTime.MinValue;
            return;
        }

        if (frame is null)
        {
            _nextCaptureUtc = DateTime.UtcNow + TimeSpan.FromSeconds(1d);
            return;
        }

        _blurFrame = frame;
        if (_blurImages.Count == 0)
        {
            _hasRenderState = false;
            return;
        }

        foreach (var image in _blurImages)
        {
            image.Source = frame;
        }
    }

    private void ResetBlurCapture()
    {
        if (_captureRequest is null && _blurFrame is null && !_blurCaptureLifecycle.IsCaptureInProgress)
        {
            return;
        }

        _captureRequest = null;
        _blurFrame = null;
        _nextCaptureUtc = DateTime.MinValue;
        _blurCaptureLifecycle.Reset();
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
            _ => CreateBlurFallbackBrush(baseColor, appearance),
        };

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private static Brush CreateWetGlassTextureBrush()
    {
        const int textureWidth = 640;
        const int textureHeight = 448;
        var random = new Random(19770519);
        using var bitmap = new System.Drawing.Bitmap(
            textureWidth,
            textureHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var shadowBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(34, 8, 28, 38));
            using var highlightBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(118, 255, 255, 255));
            using var rimPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(62, 214, 239, 246), 1.15f);
            using var streakShadowPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(38, 7, 35, 48), 2.4f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };
            using var streakHighlightPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(72, 238, 252, 255), 0.9f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };

            for (var index = 0; index < 104; index++)
            {
                var radius = index % 13 == 0
                    ? 6.4f + ((float)random.NextDouble() * 7f)
                    : 1.4f + ((float)random.NextDouble() * 4.4f);
                var verticalRadius = radius * (0.68f + ((float)random.NextDouble() * 0.62f));
                var centerX = 15f + ((float)random.NextDouble() * (textureWidth - 30f));
                var centerY = 13f + ((float)random.NextDouble() * (textureHeight - 26f));
                var bounds = new System.Drawing.RectangleF(
                    centerX - radius,
                    centerY - verticalRadius,
                    radius * 2f,
                    verticalRadius * 2f);

                graphics.FillEllipse(
                    shadowBrush,
                    bounds.X + 1.4f,
                    bounds.Y + 2f,
                    bounds.Width + 0.7f,
                    bounds.Height + 0.7f);
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddEllipse(bounds);
                    using var dropletBrush = new System.Drawing.Drawing2D.PathGradientBrush(path)
                    {
                        CenterColor = System.Drawing.Color.FromArgb(42, 255, 255, 255),
                        CenterPoint = new System.Drawing.PointF(
                            centerX - (radius * 0.24f),
                            centerY - (verticalRadius * 0.28f)),
                        SurroundColors = [System.Drawing.Color.FromArgb(44, 20, 63, 82)],
                    };
                    graphics.FillPath(dropletBrush, path);
                    graphics.DrawPath(rimPen, path);
                }

                var highlightRadius = Math.Max(0.65f, radius * 0.13f);
                graphics.FillEllipse(
                    highlightBrush,
                    centerX - (radius * 0.34f) - highlightRadius,
                    centerY - (verticalRadius * 0.34f) - (highlightRadius * 0.72f),
                    highlightRadius * 2f,
                    highlightRadius * 1.44f);

                if (index % 23 == 0)
                {
                    var streakLength = 20f + ((float)random.NextDouble() * 46f);
                    var startX = centerX + 1.3f;
                    var startY = centerY + (verticalRadius * 0.72f);
                    var endX = startX - 2f;
                    var endY = Math.Min(textureHeight, startY + streakLength);
                    graphics.DrawLine(streakShadowPen, startX, startY, endX, endY);
                    graphics.DrawLine(streakHighlightPen, startX - 1.2f, startY, endX - 1.2f, endY);
                }
            }
        }

        var bitmapRect = new System.Drawing.Rectangle(0, 0, textureWidth, textureHeight);
        var bitmapData = bitmap.LockBits(
            bitmapRect,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        BitmapSource bitmapSource;
        try
        {
            bitmapSource = BitmapSource.Create(
                textureWidth,
                textureHeight,
                96d,
                96d,
                PixelFormats.Pbgra32,
                palette: null,
                bitmapData.Scan0,
                Math.Abs(bitmapData.Stride) * textureHeight,
                bitmapData.Stride);
            bitmapSource.Freeze();
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        var brush = new ImageBrush(bitmapSource)
        {
            Stretch = Stretch.UniformToFill,
        };
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBlurFallbackBrush(
        MediaColor baseColor,
        MaskSurfaceAppearance appearance)
    {
        var neutralFrost = MediaColor.FromRgb(216, 222, 225);
        var surface = Blend(baseColor, neutralFrost, appearance.MidtoneBlend);
        var highlight = Blend(baseColor, MediaColor.FromRgb(255, 255, 255), appearance.HighlightBlend);
        var shadow = Blend(baseColor, MediaColor.FromRgb(0, 0, 0), appearance.ShadowBlend);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0d, 0d),
            EndPoint = new Point(1d, 1d),
        };
        brush.GradientStops.Add(new GradientStop(highlight, 0d));
        brush.GradientStops.Add(new GradientStop(surface, 0.52d));
        brush.GradientStops.Add(new GradientStop(shadow, 1d));
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
        double overlayWidth,
        double overlayHeight,
        double cornerRadius = 14d)
    {
        var appearance = MaskAppearancePolicy.Resolve(style, strength);
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

        if (UsesLiveWindowCapture(style) && _blurFrame is not null)
        {
            AddLiveCaptureRegion(style, geometry, maskColor, appearance, overlayWidth, overlayHeight);
            return;
        }

        var shape = new Path
        {
            Data = geometry,
            Fill = BuildBackground(style, maskColor, appearance),
            Opacity = appearance.OverlayOpacity,
            IsHitTestVisible = false,
        };

        _canvas.Children.Add(shape);
        if (style == MaskStyle.WetGlass)
        {
            AddWetGlassLayer(geometry, appearance);
        }
    }

    private void AddLiveCaptureRegion(
        MaskStyle style,
        Geometry geometry,
        MaskColorOption maskColor,
        MaskSurfaceAppearance appearance,
        double overlayWidth,
        double overlayHeight)
    {
        var image = new WpfImage
        {
            Source = _blurFrame,
            Width = overlayWidth,
            Height = overlayHeight,
            Stretch = Stretch.Fill,
            Clip = geometry,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(image, 0d);
        Canvas.SetTop(image, 0d);
        _canvas.Children.Add(image);
        _blurImages.Add(image);

        var tint = new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(GetMaskBaseColor(maskColor)),
            Opacity = appearance.TintOpacity,
            IsHitTestVisible = false,
        };
        _canvas.Children.Add(tint);

        if (style == MaskStyle.WetGlass)
        {
            AddWetGlassLayer(geometry, appearance);
        }
    }

    private void AddWetGlassLayer(Geometry geometry, MaskSurfaceAppearance appearance)
    {
        var droplets = new Path
        {
            Data = geometry,
            Fill = WetGlassTextureBrush,
            Opacity = 0.36d + (appearance.SmoothedIntensity * 0.34d),
            IsHitTestVisible = false,
        };
        _canvas.Children.Add(droplets);
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

    private readonly record struct BlurCaptureRequest(
        nint WindowHandle,
        ScreenRect Bounds,
        double DownsampleFactor,
        MaskStyle Style);
}
