using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Interop;
using PrivacyMask.Windows.Models;

namespace PrivacyMask.Windows.Services;

public sealed class DesktopWindowInspector
{
    public WindowDiscoveryResult Capture()
    {
        var foregroundHandle = NativeMethods.GetForegroundWindow();
        var windows = new List<WindowSnapshot>();

        NativeMethods.EnumWindows((handle, _) =>
        {
            var snapshot = TryCreateSnapshot(handle, foregroundHandle);
            if (snapshot is not null)
            {
                windows.Add(snapshot);
            }

            return true;
        }, nint.Zero);

        return new WindowDiscoveryResult
        {
            ForegroundHandle = foregroundHandle,
            Windows = windows,
        };
    }

    public WindowSnapshot? TryGetWindow(nint handle)
    {
        if (handle == nint.Zero)
        {
            return null;
        }

        return TryCreateSnapshot(handle, NativeMethods.GetForegroundWindow());
    }

    public bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static WindowSnapshot? TryCreateSnapshot(nint handle, nint foregroundHandle)
    {
        if (handle == nint.Zero || !NativeMethods.IsWindowVisible(handle))
        {
            return null;
        }

        var exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        if ((exStyle & NativeMethods.WsExToolWindow) == NativeMethods.WsExToolWindow)
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var screenRect = new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (screenRect.IsEmpty)
        {
            return null;
        }

        var processName = TryGetProcessName(handle);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return new WindowSnapshot
        {
            Handle = handle,
            ProcessName = processName,
            Title = GetWindowText(handle),
            ClassName = GetClassName(handle),
            Bounds = screenRect,
            IsVisible = true,
            IsForeground = handle == foregroundHandle,
            IsMinimized = NativeMethods.IsIconic(handle),
        };
    }

    private static string TryGetProcessName(nint handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
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
}
