using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrivacyMask.Windows.Models;

namespace PrivacyMask.App.ViewModels;

public sealed class ApplicationCandidateViewModel
{
    public ApplicationCandidateViewModel(DesktopApplicationCandidate candidate)
    {
        Candidate = candidate;
        Icon = LoadIcon(candidate.ExecutablePath);
    }

    public DesktopApplicationCandidate Candidate { get; }

    public ImageSource Icon { get; }

    public string DisplayName => Candidate.DisplayName;

    public string ProcessSummary => $"Process: {Candidate.ProcessName}.exe";

    public string WindowSummary => Candidate.IsRunning
        ? $"Window: {Candidate.WindowTitle}"
        : "The app will be protected the next time it runs.";

    private static ImageSource LoadIcon(string executablePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                image.Freeze();
                return image;
            }
        }
        catch
        {
        }

        using var fallbackIcon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        var fallbackImage = Imaging.CreateBitmapSourceFromHIcon(
            fallbackIcon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        fallbackImage.Freeze();
        return fallbackImage;
    }
}
