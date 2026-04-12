namespace PrivacyMask.Core.Models;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool IntersectsWith(ScreenRect other)
    {
        return !Intersect(other).IsEmpty;
    }

    public ScreenRect Intersect(ScreenRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return new ScreenRect(left, top, right, bottom);
    }
}
