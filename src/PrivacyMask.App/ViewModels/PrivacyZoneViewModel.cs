using PrivacyMask.Core.Models;

namespace PrivacyMask.App.ViewModels;

public sealed class PrivacyZoneViewModel : ObservableObject
{
    private string _displayName;
    private bool _enabled;
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private double _strength;
    private MaskStyle _style;
    private bool _revealOnHover;
    private bool _hideDuringTemporaryReveal;
    private readonly string _zoneId;
    private readonly ZoneAnchor _anchor;

    public PrivacyZoneViewModel(PrivacyZone zone)
    {
        _zoneId = zone.ZoneId;
        _anchor = zone.Anchor;
        _displayName = zone.DisplayName;
        _enabled = zone.Enabled;
        _x = zone.RelativeRect.X;
        _y = zone.RelativeRect.Y;
        _width = zone.RelativeRect.Width;
        _height = zone.RelativeRect.Height;
        _strength = zone.Strength;
        _style = zone.Style;
        _revealOnHover = zone.Behavior.HasFlag(ZoneBehavior.RevealOnHover);
        _hideDuringTemporaryReveal = zone.Behavior.HasFlag(ZoneBehavior.HideDuringTemporaryReveal);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public double Strength
    {
        get => _strength;
        set => SetProperty(ref _strength, value);
    }

    public MaskStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value);
    }

    public bool RevealOnHover
    {
        get => _revealOnHover;
        set => SetProperty(ref _revealOnHover, value);
    }

    public bool HideDuringTemporaryReveal
    {
        get => _hideDuringTemporaryReveal;
        set => SetProperty(ref _hideDuringTemporaryReveal, value);
    }

    public PrivacyZone ToModel()
    {
        var behavior = ZoneBehavior.None;
        if (RevealOnHover)
        {
            behavior |= ZoneBehavior.RevealOnHover;
        }

        if (HideDuringTemporaryReveal)
        {
            behavior |= ZoneBehavior.HideDuringTemporaryReveal;
        }

        return new PrivacyZone
        {
            ZoneId = _zoneId,
            DisplayName = DisplayName,
            Anchor = _anchor,
            RelativeRect = new RelativeRect(X, Y, Width, Height).Clamp(),
            Style = Style,
            Strength = double.Clamp(Strength, 0.15d, 1d),
            Behavior = behavior,
            Enabled = Enabled,
        };
    }
}
