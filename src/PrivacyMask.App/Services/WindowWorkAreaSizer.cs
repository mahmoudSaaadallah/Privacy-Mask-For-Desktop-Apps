using System.Windows;
using PrivacyMask.Windows.Services;

namespace PrivacyMask.App.Services;

public static class WindowWorkAreaSizer
{
    public static void Fit(Window window)
    {
        var workArea = SystemParameters.WorkArea;
        var constraints = WindowSizePolicy.FitToWorkArea(
            workArea.Width,
            workArea.Height,
            window.Width,
            window.Height,
            window.MinWidth,
            window.MinHeight);

        window.MaxWidth = workArea.Width;
        window.MaxHeight = workArea.Height;
        window.MinWidth = constraints.MinWidth;
        window.MinHeight = constraints.MinHeight;
        window.Width = constraints.Width;
        window.Height = constraints.Height;
    }
}
