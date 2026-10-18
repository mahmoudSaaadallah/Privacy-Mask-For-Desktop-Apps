namespace PrivacyMask.Updates;

internal sealed record UpdateCheckHistory(int SchemaVersion = 1, DateTimeOffset? LastAttemptUtc = null,
    DateTimeOffset? RetryAfterUtc = null, DateTimeOffset? StableSuccessUtc = null, DateTimeOffset? PreviewSuccessUtc = null,
    bool RequestPending = false)
{
    // Required updates use daily successful checks and bounded retries after
    // failure. A reconnect never bypasses a persisted server Retry-After.
    internal DateTimeOffset MandatoryDueAt(DateTimeOffset now)
    {
        if (RequestPending) return DateTimeOffset.MaxValue;
        var success = StableSuccessUtc > PreviewSuccessUtc || PreviewSuccessUtc is null ? StableSuccessUtc : PreviewSuccessUtc;
        var delay = success is not null && success >= LastAttemptUtc ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(15);
        var due = LastAttemptUtc is { } attempt ? attempt + delay : now;
        return RetryAfterUtc > due ? RetryAfterUtc.Value : due;
    }

    internal bool StartupDue(DateTimeOffset now) => !RequestPending && !(RetryAfterUtc > now)
        && (LastAttemptUtc is null || now - LastAttemptUtc.Value >= TimeSpan.FromHours(24));

    internal UpdateCheckHistory Attempt(DateTimeOffset now) => this with
    {
        LastAttemptUtc = LastAttemptUtc > now ? LastAttemptUtc : now,
        RequestPending = true,
        RetryAfterUtc = RetryAfterUtc > now ? RetryAfterUtc : null,
    };

    internal UpdateCheckHistory Complete(UpdateChannel channel, UpdateCheckResult result, DateTimeOffset now)
    {
        var retry = result.RetryAt?.ToUniversalTime();
        var next = this with { RequestPending = false, RetryAfterUtc = retry > RetryAfterUtc || RetryAfterUtc is null ? retry : RetryAfterUtc };
        if (result.Status is not (UpdateCheckStatus.Available or UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoCompatibleUpdate))
            return next;
        return channel == UpdateChannel.Stable ? next with { StableSuccessUtc = now } : next with { PreviewSuccessUtc = now };
    }
}

internal interface IUpdateCheckHistoryStore
{
    IDisposable Acquire();
    Task<UpdateCheckHistory> ReadAsync(CancellationToken token);
    Task WriteAsync(UpdateCheckHistory history, CancellationToken token);
}
