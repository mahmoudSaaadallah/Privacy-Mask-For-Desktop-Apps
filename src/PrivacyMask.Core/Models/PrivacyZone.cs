namespace PrivacyMask.Core.Models;

public sealed class PrivacyZone
{
    public string ZoneId { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public ZoneAnchor Anchor { get; set; } = ZoneAnchor.Window;

    public RelativeRect RelativeRect { get; set; } = new(0d, 0d, 1d, 1d);

    public MaskStyle Style { get; set; } = MaskStyle.Blur;

    public double Strength { get; set; } = 0.82d;

    public ZoneBehavior Behavior { get; set; } = ZoneBehavior.HideDuringTemporaryReveal;

    public bool Enabled { get; set; } = true;
}
