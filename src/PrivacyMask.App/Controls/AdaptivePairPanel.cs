using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace PrivacyMask.App.Controls;

/// <summary>Two related blocks: side by side when readable, stacked otherwise.</summary>
public sealed class AdaptivePairPanel : Panel
{
    public static readonly DependencyProperty BreakWidthProperty = DependencyProperty.Register(
        nameof(BreakWidth), typeof(double), typeof(AdaptivePairPanel),
        new FrameworkPropertyMetadata(680d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptivePairPanel),
        new FrameworkPropertyMetadata(18d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty EqualColumnsProperty = DependencyProperty.Register(
        nameof(EqualColumns), typeof(bool), typeof(AdaptivePairPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double BreakWidth { get => (double)GetValue(BreakWidthProperty); set => SetValue(BreakWidthProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
    public bool EqualColumns { get => (bool)GetValue(EqualColumnsProperty); set => SetValue(EqualColumnsProperty, value); }
    public bool IsSideBySide { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count != 2) return new Size();
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : BreakWidth;
        IsSideBySide = width >= BreakWidth;
        var (firstWidth, secondWidth) = GetWidths(width);
        InternalChildren[0].Measure(new Size(firstWidth, double.PositiveInfinity));
        InternalChildren[1].Measure(new Size(secondWidth, double.PositiveInfinity));
        var first = InternalChildren[0].DesiredSize;
        var second = InternalChildren[1].DesiredSize;
        return new Size(width, IsSideBySide ? Math.Max(first.Height, second.Height) : first.Height + Gap + second.Height);
    }

    private (double First, double Second) GetWidths(double width)
    {
        if (!IsSideBySide) return (width, width);
        if (EqualColumns) return (Math.Max(0, (width - Gap) / 2), Math.Max(0, (width - Gap) / 2));
        InternalChildren[1].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var second = Math.Min(width, InternalChildren[1].DesiredSize.Width);
        return (Math.Max(0, width - Gap - second), second);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count != 2) return finalSize;
        var (firstWidth, secondWidth) = GetWidths(finalSize.Width);
        var first = InternalChildren[0];
        var second = InternalChildren[1];
        first.Arrange(new Rect(0, 0, firstWidth, first.DesiredSize.Height));
        if (IsSideBySide)
            second.Arrange(new Rect(firstWidth + Gap, 0, secondWidth, second.DesiredSize.Height));
        else
            second.Arrange(new Rect(0, first.DesiredSize.Height + Gap, secondWidth, second.DesiredSize.Height));
        return finalSize;
    }
}
