using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.App.Services;

internal sealed record UpdateCheckHistory(int SchemaVersion = 1, DateTimeOffset? LastAttemptUtc = null,
    DateTimeOffset? RetryAfterUtc = null, DateTimeOffset? StableSuccessUtc = null, DateTimeOffset? PreviewSuccessUtc = null)
{
    internal bool StartupDue(DateTimeOffset now) => !(RetryAfterUtc > now)
        && (LastAttemptUtc is null || now - LastAttemptUtc.Value >= TimeSpan.FromHours(24));

    internal UpdateCheckHistory Attempt(DateTimeOffset now) => this with
    {
        LastAttemptUtc = LastAttemptUtc > now ? LastAttemptUtc : now,
        RetryAfterUtc = RetryAfterUtc > now ? RetryAfterUtc : null,
    };

    internal UpdateCheckHistory Complete(UpdateReleaseChannel channel, UpdateCheckResult result, DateTimeOffset now)
    {
        var retry = result.RetryAt?.ToUniversalTime();
        var next = this with { RetryAfterUtc = retry > RetryAfterUtc || RetryAfterUtc is null ? retry : RetryAfterUtc };
        if (result.Status is not (UpdateCheckStatus.Available or UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoCompatibleUpdate))
            return next;
        return channel == UpdateReleaseChannel.Stable ? next with { StableSuccessUtc = now } : next with { PreviewSuccessUtc = now };
    }
}

internal interface IUpdateCheckHistoryStore
{
    Task<UpdateCheckHistory> ReadAsync(CancellationToken token);
    Task WriteAsync(UpdateCheckHistory history, CancellationToken token);
}
