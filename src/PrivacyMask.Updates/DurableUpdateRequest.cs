namespace PrivacyMask.Updates;

/// <summary>Shared retry bookkeeping for pre-apply checks in the App and native launcher.</summary>
internal static class DurableUpdateRequest
{
    internal static async Task<UpdateCheckResult> RunAsync(IUpdateCheckHistoryStore store, UpdateChannel channel,
        Func<CancellationToken, Task<UpdateCheckResult>> request, TimeProvider time, CancellationToken token)
    {
        UpdateCheckHistory? admitted = null;
        try
        {
            using var lease = store.Acquire();
            var history = await store.ReadAsync(token).ConfigureAwait(false);
            if (history.RequestPending) return Unavailable();
            if (history.RetryAfterUtc > time.GetUtcNow()) return new(UpdateCheckStatus.RateLimited, RetryAt: history.RetryAfterUtc);
            history = history.Attempt(time.GetUtcNow());
            await store.WriteAsync(history, token).ConfigureAwait(false);
            admitted = history;
            try
            {
                token.ThrowIfCancellationRequested();
                var result = await request(token).ConfigureAwait(false);
                // Keep the completed outcome for cancellation-safe finalization, including server retry.
                admitted = history.Complete(channel, result, time.GetUtcNow());
                using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await store.WriteAsync(admitted, finish.Token).ConfigureAwait(false);
                admitted = null;
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Finalize while still holding the same history lease. Unknown writes stay fail-closed.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await store.WriteAsync(admitted! with { RequestPending = false }, cleanup.Token).ConfigureAwait(false);
                admitted = null;
                throw;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    private static UpdateCheckResult Unavailable() => new(UpdateCheckStatus.UnableToCheck, Reason: "local-check-history-unavailable");
}
