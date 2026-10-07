using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Interop;
using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public sealed class DesktopWindowInspector
{
    private readonly Dictionary<nint, CachedProcessIdentity> _processIdentityCache = [];
    private readonly HashSet<nint> _observedHandles = [];
    private readonly List<nint> _staleHandles = [];
    private readonly List<DesktopWindowSnapshot> _windows = [];
    private readonly List<WindowSnapshot> _candidateWindows = [];

    public WindowDiscoveryResult Capture(WindowProcessFilter? processFilter = null)
    {
        var foregroundHandle = NativeMethods.GetForegroundWindow();
        var zOrderIndex = 0;
        _observedHandles.Clear();
        _windows.Clear();
        _candidateWindows.Clear();

        NativeMethods.EnumWindows((handle, _) =>
        {
            var wasCaptured = TryCreateSnapshot(
                handle,
                foregroundHandle,
                zOrderIndex,
                processFilter,
                out var desktopSnapshot,
                out var candidateSnapshot);
            zOrderIndex++;
            if (wasCaptured)
            {
                _windows.Add(desktopSnapshot);
                if (candidateSnapshot is not null)
                {
                    _candidateWindows.Add(candidateSnapshot);
                }
            }

            return true;
        }, nint.Zero);

        PruneProcessIdentityCache();

        return new WindowDiscoveryResult
        {
            ForegroundHandle = foregroundHandle,
            Windows = _windows,
            CandidateWindows = _candidateWindows,
        };
    }

    public WindowSnapshot? TryGetWindow(nint handle)
    {
        if (handle == nint.Zero)
        {
            return null;
        }

        return TryCreateSnapshot(
            handle,
            NativeMethods.GetForegroundWindow(),
            int.MaxValue,
            processFilter: null,
            out _,
            out var candidateSnapshot)
            ? candidateSnapshot
            : null;
    }

    public bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private bool TryCreateSnapshot(
        nint handle,
        nint foregroundHandle,
        int zOrderIndex,
        WindowProcessFilter? processFilter,
        out DesktopWindowSnapshot desktopSnapshot,
        out WindowSnapshot? candidateSnapshot)
    {
        desktopSnapshot = default;
        candidateSnapshot = null;
        if (handle == nint.Zero || !NativeMethods.IsWindowVisible(handle))
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        if ((exStyle & NativeMethods.WsExToolWindow) == NativeMethods.WsExToolWindow)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            return false;
        }

        var screenRect = new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (screenRect.IsEmpty)
        {
            return false;
        }

        var isMinimized = NativeMethods.IsIconic(handle);
        desktopSnapshot = new DesktopWindowSnapshot(
            handle,
            screenRect,
            zOrderIndex,
            IsVisible: true,
            isMinimized);

        var processName = TryGetProcessName(handle);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return true;
        }

        var isCandidate = processFilter is null || processFilter.IsMatch(processName);
        if (!isCandidate)
        {
            return true;
        }

        candidateSnapshot = new WindowSnapshot
        {
            Handle = handle,
            ProcessName = processName,
            Title = GetWindowText(handle),
            ClassName = GetClassName(handle),
            Bounds = screenRect,
            ZOrderIndex = zOrderIndex,
            IsVisible = true,
            IsForeground = handle == foregroundHandle,
            IsMinimized = isMinimized,
        };
        return true;
    }

    private string TryGetProcessName(nint handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return string.Empty;
        }

        _observedHandles.Add(handle);
        if (_processIdentityCache.TryGetValue(handle, out var cached) && cached.ProcessId == processId)
        {
            return cached.ProcessName;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            _processIdentityCache[handle] = new CachedProcessIdentity(processId, processName);
            return processName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void PruneProcessIdentityCache()
    {
        _staleHandles.Clear();
        foreach (var handle in _processIdentityCache.Keys)
        {
            if (!_observedHandles.Contains(handle))
            {
                _staleHandles.Add(handle);
            }
        }

        foreach (var handle in _staleHandles)
        {
            _processIdentityCache.Remove(handle);
        }
    }

    private static string GetWindowText(nint handle)
    {
        var capacity = NativeMethods.GetWindowTextLength(handle);
        var builder = new StringBuilder(Math.Max(capacity + 1, 260));
        _ = NativeMethods.GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static string GetClassName(nint handle)
    {
        var builder = new StringBuilder(260);
        _ = NativeMethods.GetClassName(handle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private readonly record struct CachedProcessIdentity(uint ProcessId, string ProcessName);
}
