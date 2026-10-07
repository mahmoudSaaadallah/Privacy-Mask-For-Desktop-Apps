# Changelog

## Unreleased

- Start every app launch with protection enabled instead of restoring a paused or panic runtime state
- Change panic mode to an opaque black full-window mask that disables reveal gestures
- Prevent the pause hotkey from bypassing an active panic mask
- Update settings schema to version 6 while preserving compatibility with existing files
- Save settings atomically, serialize concurrent writes, and avoid unchanged startup rewrites
- Keep a last-known-good settings backup and preserve malformed JSON during recovery
- Release timers, hotkeys, overlays, windows, and the tray icon reliably during shutdown
- Report global shortcut conflicts in the tray and beside the affected setting
- Handle startup, settings-save, and launch-at-sign-in failures without unhandled exceptions
- Inspect detailed window metadata only for processes that can match enabled profiles
- Cache process identity and effective mask zones across unchanged refresh cycles
- Skip rebuilding overlay brushes and geometry when the rendered state is unchanged
- Use a slower idle refresh cadence with immediate pause and panic updates
- Reuse hot-path collections and lightweight desktop-window snapshots to reduce allocations
- Remove generated Windows executables and runtime files from source control
- Package installable Windows ZIPs with SHA-256 checksums for tagged GitHub Releases
- Document release installation, portable use, updates, and uninstallation

## 1.0.0

- Initial Windows desktop release of PrivacyMask
- WPF tray application with overlay masking for WhatsApp Desktop and Telegram Desktop
- Hover reveal support
- Configurable mask darkness and hover window size
- Local JSON settings, onboarding flow, and hotkeys
