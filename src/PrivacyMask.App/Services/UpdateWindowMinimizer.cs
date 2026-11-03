using PrivacyMask.Core.Models;

namespace PrivacyMask.App.Services;

internal static class UpdateWindowMinimizer
{
    internal static async Task<bool> RunAsync(Func<IReadOnlyList<WindowSnapshot>> capture,
        Func<WindowSnapshot, bool> minimize, Func<bool> canContinue, Func<Task>? wait = null)
    {
        var requested = new HashSet<nint>();
        // ShowWindowAsync only acknowledges a request. Complete enumeration must
        // confirm the windows actually closed or minimized before masks can unlock.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!canContinue()) return false;
            var windows = capture();
            if (windows.Count == 0) return canContinue();
            foreach (var window in windows)
            {
                if (!canContinue()) return false;
                if (requested.Add(window.Handle) && !minimize(window)) return false;
            }
            await (wait?.Invoke() ?? Task.Delay(100));
        }
        return false;
    }
}
