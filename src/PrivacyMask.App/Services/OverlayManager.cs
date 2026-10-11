using System;
using System.Collections.Generic;
using PrivacyMask.App.Windows;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using Point = System.Windows.Point;

namespace PrivacyMask.App.Services;

public sealed class OverlayManager : IDisposable
{
    private readonly Dictionary<nint, PrivacyOverlayWindow> _overlays = [];
    private readonly HashSet<nint> _activeHandles = [];
    private readonly List<nint> _staleHandles = [];

    public void Update(
        IReadOnlyList<TrackedWindow> windows,
        RuntimeMode mode,
        ProfileProtectionState profileProtectionState,
        string? temporaryRevealProfileId,
        Point cursorScreenPoint)
    {
        _activeHandles.Clear();

        foreach (var window in windows)
        {
            _activeHandles.Add(window.Snapshot.Handle);
            if (!_overlays.TryGetValue(window.Snapshot.Handle, out var overlay))
            {
                overlay = new PrivacyOverlayWindow();
                _overlays[window.Snapshot.Handle] = overlay;
            }

            var effectiveMode = profileProtectionState.ResolveMode(window.Profile.ProfileId, mode);
            var temporaryRevealHeld = effectiveMode != RuntimeMode.Panic && string.Equals(
                window.Profile.ProfileId,
                temporaryRevealProfileId,
                StringComparison.OrdinalIgnoreCase);
            overlay.UpdateOverlay(window, effectiveMode, temporaryRevealHeld, cursorScreenPoint);
        }

        _staleHandles.Clear();
        foreach (var handle in _overlays.Keys)
        {
            if (!_activeHandles.Contains(handle))
            {
                _staleHandles.Add(handle);
            }
        }

        foreach (var staleHandle in _staleHandles)
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
