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

## 1.0.0

- Initial Windows desktop release of PrivacyMask
- WPF tray application with overlay masking for WhatsApp Desktop and Telegram Desktop
- Hover reveal support
- Configurable mask darkness and hover window size
- Local JSON settings, onboarding flow, and hotkeys
