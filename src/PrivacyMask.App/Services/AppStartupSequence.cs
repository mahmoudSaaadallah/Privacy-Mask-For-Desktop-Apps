namespace PrivacyMask.App.Services;

internal static class AppStartupSequence
{
    internal static async Task RunAsync(
        Func<Task> initializeShell,
        Func<Task>? confirmUpdate,
        Func<Task>? completeInteractiveStartup)
    {
        ArgumentNullException.ThrowIfNull(initializeShell);
        if ((confirmUpdate is null) != (completeInteractiveStartup is null))
            throw new ArgumentException("Update startup milestones must be supplied together.");

        await initializeShell();
        if (confirmUpdate is null) return;
        await confirmUpdate();
        await completeInteractiveStartup!();
    }
}
