using System;
using System.Collections.Generic;
using System.Linq;
using PrivacyMask.App.Windows;
using PrivacyMask.Core.Models;
using Point = System.Windows.Point;

namespace PrivacyMask.App.Services;

public sealed class OverlayManager : IDisposable
{
    private readonly Dictionary<nint, PrivacyOverlayWindow> _overlays = [];

    public void Update(IReadOnlyList<TrackedWindow> windows, RuntimeMode mode, bool temporaryRevealHeld, Point cursorScreenPoint)
    {
        var activeHandles = windows.Select(window => window.Snapshot.Handle).ToHashSet();

        foreach (var window in windows)
        {
            if (!_overlays.TryGetValue(window.Snapshot.Handle, out var overlay))
            {
                overlay = new PrivacyOverlayWindow();
                _overlays[window.Snapshot.Handle] = overlay;
            }

            overlay.UpdateOverlay(window, mode, temporaryRevealHeld, cursorScreenPoint);
        }

        foreach (var staleHandle in _overlays.Keys.Where(handle => !activeHandles.Contains(handle)).ToList())
        {
            _overlays[staleHandle].HideOverlay();
            _overlays[staleHandle].Close();
            _overlays.Remove(staleHandle);
        }
    }

    public void HideAll()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.HideOverlay();
        }
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.HideOverlay();
            overlay.Close();
        }

        _overlays.Clear();
    }
}
