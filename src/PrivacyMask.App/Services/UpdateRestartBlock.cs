namespace PrivacyMask.App.Services;

internal enum UpdateRestartBlockKind { Panic, SettingsEdits, VisibleWindows, InspectionUnavailable }

internal sealed record UpdateBlockingApplication(string ProfileId, string DisplayName);
internal sealed record UpdateBlockingWindow(bool IsVisible, bool IsMinimized, UpdateBlockingApplication? Application);

/// <summary>Ephemeral presentation snapshot, never restart authority or persisted update data.</summary>
internal sealed record UpdateRestartBlock(UpdateRestartBlockKind Kind, IReadOnlyList<UpdateBlockingApplication> Applications)
{
    internal bool CanReviewApplications => Kind == UpdateRestartBlockKind.VisibleWindows;

    internal string Message => Kind switch
    {
        UpdateRestartBlockKind.Panic => "Required update is waiting for Panic to be unlocked using your PIN. It will continue automatically.",
        UpdateRestartBlockKind.SettingsEdits => "Required update is waiting for unsaved or invalid settings edits. Save or discard your changes normally to continue.",
        UpdateRestartBlockKind.VisibleWindows => $"Close or minimize all windows of: {ApplicationNames}. Pausing protection does not close a window. The update will continue automatically.",
        _ => "Required update is waiting because window safety could not be checked. Protection remains active; PrivacyMask will check again automatically.",
    };

    private string ApplicationNames => string.Join(", ", Applications.Take(3).Select(app => SafeLabel(app.DisplayName)))
        + (Applications.Count > 3 ? $" (+{Applications.Count - 3} more applications)" : "");

    private static string SafeLabel(string name)
    {
        var label = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return label.Length == 0 ? "Unnamed application" : label.Length > 120 ? label[..120] + "…" : label;
    }

    internal static UpdateRestartBlock? Assess(bool hasPanic, bool hasEdits,
        Func<IEnumerable<UpdateBlockingWindow>> inspect)
    {
        if (hasPanic) return new(UpdateRestartBlockKind.Panic, []);
        if (hasEdits) return new(UpdateRestartBlockKind.SettingsEdits, []);
        try
        {
            var applications = inspect().Where(window => window.IsVisible && !window.IsMinimized && window.Application is not null)
                .Select(window => window.Application!).DistinctBy(app => app.ProfileId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(app => app.ProfileId, StringComparer.OrdinalIgnoreCase).ToArray();
            return applications.Length == 0 ? null : new(UpdateRestartBlockKind.VisibleWindows, applications);
        }
        catch { return new(UpdateRestartBlockKind.InspectionUnavailable, []); }
    }
}
