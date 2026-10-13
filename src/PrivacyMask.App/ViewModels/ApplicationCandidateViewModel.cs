using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrivacyMask.Windows.Models;
using PrivacyMask.App.Services;

namespace PrivacyMask.App.ViewModels;

public sealed class ApplicationCandidateViewModel
{
    public ApplicationCandidateViewModel(DesktopApplicationCandidate candidate)
    {
        Candidate = candidate;
        Icon = ApplicationIconCache.Get(candidate.ExecutablePath);
    }

    public DesktopApplicationCandidate Candidate { get; }

    public ImageSource Icon { get; }

    public string DisplayName => Candidate.DisplayName;

    public string ProcessSummary => $"Process: {Candidate.ProcessName}.exe";

    public string WindowSummary => Candidate.IsRunning
        ? $"Window: {Candidate.WindowTitle}"
        : "The app will be protected the next time it runs.";

}
