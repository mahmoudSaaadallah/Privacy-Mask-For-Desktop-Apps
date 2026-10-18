using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.ViewModels;

/// <summary>Session-only status, deliberately outside the persisted settings edit session.</summary>
public sealed class UpdateStatusViewModel : ObservableObject
{
    private UpdateCheckResult? _result;
    private bool _isChecking;
    private bool _isStopped;
    private UpdateReleaseChannel? _checkedChannel;
    private readonly Dictionary<UpdateReleaseChannel, DateTimeOffset> _successfulChecks = [];

    public bool IsChecking => _isChecking;
    public bool CanCheck => !_isChecking && !_isStopped;
    public string CheckLabel => _isChecking ? "Checking…" : "Check for updates";
    public string StatusText => _isChecking ? "Checking signed releases…" : _result?.Status switch
    {
        UpdateCheckStatus.Available => $"Update available: {_result.Candidate?.Manifest.Version}",
        UpdateCheckStatus.UpToDate => "You are up to date on the checked channel.",
        UpdateCheckStatus.NoCompatibleUpdate => "No compatible update is available for this installation.",
        UpdateCheckStatus.NotConfigured => "Signed updates are not configured in this testing build. Use View releases to update manually.",
        UpdateCheckStatus.ManualUpdateRequired => "Use a release installer to update this source, portable, or unsupported installation.",
        UpdateCheckStatus.RateLimited => "GitHub asked us to wait. Try again after the retry time below.",
        UpdateCheckStatus.Incomplete => "The release check was incomplete. Your update status could not be confirmed.",
        UpdateCheckStatus.InProgress => "An update check is already running.",
        UpdateCheckStatus.InvalidContext => "This installation could not be checked safely. Use View releases.",
        UpdateCheckStatus.UnableToCheck => "Unable to check for updates. Your current application is unchanged.",
        _ => "No update check has completed in this session.",
    };
    public string LastCheckText => _checkedChannel is null ? "Last successful check this session: never"
        : $"Checked channel: {_checkedChannel}. Last successful check this session: {(_successfulChecks.TryGetValue(_checkedChannel.Value, out var last) ? last.ToLocalTime().ToString("g") : "never")}";
    public string RetryText => _result?.RetryAt is { } retry ? $"Retry after {retry.ToLocalTime():g}" : "";
    public string ReleaseNotes => _result?.Candidate?.ReleaseNotes is { } notes ? notes[..Math.Min(notes.Length, 4096)] : "";
    public bool HasCandidate => _result?.Status == UpdateCheckStatus.Available && _result.Candidate is not null;
    public string CandidateDetail => HasCandidate
        ? $"{_result!.Candidate!.Manifest.Package.Size / 1048576d:0.0} MiB. In-app download and installation are not enabled in this build. Use View releases."
        : "";

    internal void Begin()
    {
        _isChecking = true;
        _result = null;
        Notify();
    }

    internal void Complete(UpdateCheckResult result, UpdateReleaseChannel channel, DateTimeOffset now)
    {
        _isChecking = false;
        _checkedChannel = channel;
        _result = result;
        if (result.Status is UpdateCheckStatus.Available or UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoCompatibleUpdate)
            _successfulChecks[channel] = now;
        Notify();
    }

    internal void Stop()
    {
        _isStopped = true;
        _isChecking = false;
        Notify();
    }

    private void Notify()
    {
        foreach (var property in new[] { nameof(IsChecking), nameof(CanCheck), nameof(CheckLabel), nameof(StatusText),
            nameof(LastCheckText), nameof(RetryText), nameof(ReleaseNotes), nameof(HasCandidate), nameof(CandidateDetail) })
            RaisePropertyChanged(property);
    }
}
