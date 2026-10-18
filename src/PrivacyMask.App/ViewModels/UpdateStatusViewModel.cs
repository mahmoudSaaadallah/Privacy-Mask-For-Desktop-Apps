using PrivacyMask.Core.Models;
using PrivacyMask.Updates;
using PrivacyMask.App.Services;

namespace PrivacyMask.App.ViewModels;

/// <summary>Discovery status/history, deliberately outside the settings edit session.</summary>
public sealed class UpdateStatusViewModel : ObservableObject
{
    private UpdateCheckResult? _result;
    private bool _isChecking;
    private bool _isStopped;
    private bool _isPaused;
    private bool _isCancelled;
    private bool _isDownloading;
    private bool _isDiscarding;
    private bool _downloadReady;
    private bool _cleanupBlocked;
    private string _downloadText = "";
    private double _downloadPercent;
    private DateTimeOffset? _candidateCheckedUtc;
    private UpdateReleaseChannel? _checkedChannel;
    private readonly Dictionary<UpdateReleaseChannel, DateTimeOffset> _successfulChecks = [];

    public bool IsChecking => _isChecking;
    public bool CanCheck => !_isChecking && !_isStopped && !_isPaused && !_downloadReady;
    public bool CanDownload => CanCheck && HasCandidate && !_cleanupBlocked;
    public bool CanCancelDownload => _isDownloading && !_isStopped && !_isPaused;
    public bool CanDiscardDownload => _downloadReady && !_isChecking && !_isStopped && !_isPaused;
    public bool HasDownloadStatus => _downloadText.Length != 0;
    public bool IsDownloading => _isDownloading;
    public double DownloadPercent => _downloadPercent;
    public string DownloadText => _downloadText;
    internal UpdateCandidate? Candidate => HasCandidate ? _result?.Candidate : null;
    internal UpdateReleaseChannel? CandidateChannel => _checkedChannel;
    internal DateTimeOffset? CandidateCheckedUtc => _candidateCheckedUtc;
    public string CheckLabel => _isDownloading ? "Download in progress…" : _isDiscarding ? "Discarding…" : _isChecking ? "Checking…" : "Check for updates";
    public string StatusText => _isDiscarding ? "Removing only this session's verified download." : _isDownloading ? "Preparing a verified download. Protection remains active." : _isChecking ? "Checking signed releases…" : _isCancelled ? "Update check cancelled. Your application is unchanged." : _result?.Status switch
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
        UpdateCheckStatus.UnableToCheck when _result.Reason == "local-check-history-unavailable" => "Local update-check history is unavailable. No update was installed. Use View releases.",
        UpdateCheckStatus.UnableToCheck => "Unable to check for updates. Your current application is unchanged.",
        _ => "No update check has completed in this session.",
    };
    public string LastCheckText => _checkedChannel is null ? "Last successful check: never"
        : $"Checked channel: {_checkedChannel}. Last successful check: {(_successfulChecks.TryGetValue(_checkedChannel.Value, out var last) ? last.ToLocalTime().ToString("g") : "never")}";
    public string RetryText => _result?.RetryAt is { } retry ? $"Retry after {retry.ToLocalTime():g}" : "";
    public string ReleaseNotes => _result?.Candidate?.ReleaseNotes is { } notes ? notes[..Math.Min(notes.Length, 4096)] : "";
    public bool HasCandidate => _result?.Status == UpdateCheckStatus.Available && _result.Candidate is not null;
    public string CandidateDetail => HasCandidate
        ? $"{_result!.Candidate!.Manifest.Package.Size / 1048576d:0.0} MiB. Download verifies the package only; installation is not enabled in this build."
        : "";

    internal void Begin()
    {
        _isChecking = true;
        _isCancelled = false;
        _result = null;
        _candidateCheckedUtc = null;
        Notify();
    }

    internal void Complete(UpdateCheckResult result, UpdateReleaseChannel channel, DateTimeOffset now)
    {
        _isChecking = false;
        _isCancelled = false;
        _checkedChannel = channel;
        _result = result;
        _isDownloading = false;
        _isDiscarding = false;
        _candidateCheckedUtc = HasCandidate ? now : null;
        if (result.Status is UpdateCheckStatus.Available or UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoCompatibleUpdate)
            _successfulChecks[channel] = now;
        Notify();
    }

    internal void Stop()
    {
        _isStopped = true;
        _isChecking = false;
        _isDownloading = false;
        Notify();
    }

    internal void LoadHistory(UpdateCheckHistory history, UpdateReleaseChannel channel)
    {
        if (history.StableSuccessUtc is { } stable) _successfulChecks[UpdateReleaseChannel.Stable] = stable;
        if (history.PreviewSuccessUtc is { } preview) _successfulChecks[UpdateReleaseChannel.Preview] = preview;
        _checkedChannel = channel;
        RaisePropertyChanged(nameof(LastCheckText));
    }

    internal void Cancel()
    {
        _isChecking = false;
        _isCancelled = true;
        _result = null;
        _isDownloading = false;
        _candidateCheckedUtc = null;
        Notify();
    }

    internal void SetPaused(bool paused)
    {
        _isPaused = paused;
        Notify();
    }

    internal void BeginDownload()
    {
        _isChecking = true;
        _isDownloading = true;
        _downloadPercent = 0;
        _downloadText = "Preparing download…";
        Notify();
    }

    internal void BeginDiscard()
    {
        _isChecking = true;
        _isDiscarding = true;
        _downloadText = "Removing this verified download…";
        Notify();
    }

    internal void DownloadProgress(UpdateDownloadProgress progress)
    {
        if (!_isDownloading || _isStopped || progress.TotalBytes <= 0 || progress.Bytes < 0) return;
        _downloadPercent = Math.Clamp(100d * progress.Bytes / progress.TotalBytes, 0, 100);
        _downloadText = progress.Phase switch
        {
            UpdateDownloadPhase.Downloading => $"Downloading: {progress.Bytes / 1048576d:0.0} / {progress.TotalBytes / 1048576d:0.0} MiB",
            UpdateDownloadPhase.Verifying => "Verifying package…",
            UpdateDownloadPhase.Extracting => "Verifying application…",
            _ => "Finishing verification…", // A progress callback is never proof of readiness.
        };
        Notify();
    }

    internal void FinishDownload(string message, bool ready = false, bool cleanupIncomplete = false)
    {
        _isChecking = false;
        _isDownloading = false;
        _isDiscarding = false;
        _downloadReady = ready;
        _cleanupBlocked |= cleanupIncomplete;
        _downloadText = message + (cleanupIncomplete ? " Some staging files could not be removed. Use View releases; no installed files were changed." : "");
        if (ready) _downloadPercent = 100;
        Notify();
    }

    private void Notify()
    {
        foreach (var property in new[] { nameof(IsChecking), nameof(CanCheck), nameof(CheckLabel), nameof(StatusText),
            nameof(LastCheckText), nameof(RetryText), nameof(ReleaseNotes), nameof(HasCandidate), nameof(CandidateDetail),
            nameof(CanDownload), nameof(CanCancelDownload), nameof(CanDiscardDownload), nameof(HasDownloadStatus),
            nameof(IsDownloading), nameof(DownloadPercent), nameof(DownloadText) })
            RaisePropertyChanged(property);
    }
}
