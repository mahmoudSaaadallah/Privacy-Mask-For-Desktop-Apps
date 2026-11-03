namespace PrivacyMask.App.Services;

internal static class TrayExitGuard
{
    internal static async Task TryExitAsync(ShellOperationQueue operations, Func<bool> hasPanic,
        Action reportPanic, Func<Task> shutdown)
    {
        if (hasPanic())
        {
            reportPanic();
            return;
        }

        // Prevent a new Panic action from being admitted between the final check
        // and shutdown. An already accepted action may still activate Panic.
        using var suspension = operations.TrySuspend();
        if (suspension is null && !operations.IsStopping) return;
        if (suspension is not null) await suspension.Drain;

        if (hasPanic())
        {
            reportPanic();
            return;
        }

        await shutdown();
    }
}
