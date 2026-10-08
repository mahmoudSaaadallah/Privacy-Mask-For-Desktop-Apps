# Changelog

## Unreleased

- Add a lightweight wet-glass mask with transparent condensation droplets over live blur
- Expose mask-style selection in the primary per-application settings
- Preserve the selected mask style when window width activates another adaptive preset
- Add `Ctrl + Win + ↑` to raise the focused protected app's blur strength by 5%
- Save shortcut adjustments atomically and prevent key-repeat from issuing duplicate steps
- Replace the repeated procedural frost tiles with a live blur of the protected window
- Make the 0–100% blur-strength scale progressively reveal or suppress underlying detail
- Capture bounded window frames in memory, refresh them asynchronously, and clear them when protection is hidden
- Keep a seamless opaque fallback when a protected window cannot be captured
- Preserve solid-redact and opaque panic-mask options for maximum concealment
- Replace the legacy 60–240% darkness control with a clear 0–100% blur-strength scale
- Migrate existing profile and zone intensities to settings schema version 7
- Present friendly mask-style names and percentage-based zone strength in advanced settings

## 1.1.0 - 2026-10-08

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
- Add a one-click batch installer that builds and installs directly from a source checkout
- Create Windows shortcuts correctly when the user profile path contains non-ASCII characters
- Roll back live mask previews when settings are discarded and preserve in-progress edits on reactivation
- Add keyboard shortcuts, access keys, and screen-reader metadata to the primary setup flows
- Fit settings, onboarding, and About windows to the available monitor work area
- Validate editable dimensions and privacy zones with actionable errors instead of silently changing values
- Keep advanced privacy-zone editing behind a simpler expandable section
- Make Windows updates transactional with settings preservation and automatic rollback
- Use one product version across assemblies, About, build metadata, release assets, and Git tags
- Smoke test release checksums, archive contents, executable versions, installation, shortcuts, and removal in CI

## 1.0.0

- Initial Windows desktop release of PrivacyMask
- WPF tray application with overlay masking for WhatsApp Desktop and Telegram Desktop
- Hover reveal support
- Configurable mask darkness and hover window size
- Local JSON settings, onboarding flow, and hotkeys
