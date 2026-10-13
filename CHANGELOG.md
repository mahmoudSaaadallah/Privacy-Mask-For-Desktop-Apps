# Changelog

## 2.0.0 - Unreleased

- Add six sidebar destinations sharing one settings edit session: Overview, Applications, Shortcuts, Security, Settings, and About
- Keep unsaved edits across navigation and report pending, saving, and failed-save states
- Keep settings open after Save or Discard, restore all saved values on Discard, and update previews for nested settings changes
- Prevent runtime status changes from marking settings dirty, and preserve unrelated edits when shortcut or tray changes are saved
- Introduce the approved private-view application icon across the executable, desktop windows, notification tray, and installed shortcuts
- Add shared GUI colors, spacing, cards, accessible focus outlines, and action-button styles for the upcoming sidebar redesign
- Show live per-profile states for protected, paused, focus-waiting, disabled, stopped, and missing-target applications
- Add a custom-profile relink flow that changes the target executable while preserving mask appearance, strength, zones, hover settings, and profile identity
- Persist executable paths for newly selected applications and migrate the optional field through settings schema version 12
- Let `Ctrl + Shift + P` automatically add and immediately protect the focused desktop application when it has no profile yet
- Restore a disabled matching profile from the same shortcut instead of creating a duplicate, while retaining system and security-process exclusions
- Enable mouse-hover reveal for every newly added application and migrate existing custom profiles to the same behavior
- Add a local 4–8 digit panic unlock PIN that can be created or changed from settings
- Require the PIN before disabling either a focused-application panic mask or the global tray panic mask
- Store only a salted PBKDF2-SHA256 PIN verifier and never persist the PIN itself
- Require holding `Alt` alone for 0.5 seconds before revealing the focused app; pressing any other key cancels that hold until Alt is released
- Use a 600×70 pixel hover reveal window by default and migrate only profiles that still use the previous 394×42 default
- Enable launch at sign-in by default for new installations while preserving the saved choice for existing users
- Migrate the updated profile defaults and panic PIN credential through settings schema version 11
- Move blur adjustment to `Ctrl + Alt + Shift + ↑` and `Ctrl + Alt + Shift + ↓`, migrating the previous Windows-key bindings while preserving custom shortcuts
- Scope `Ctrl + Shift + P`, `Ctrl + Shift + H`, and hold-Alt reveal to the focused protected application instead of changing every protected app
- Keep tray pause and panic mask global so an explicit global control remains available
- Enable mouse-hover reveal for new and existing custom profiles while retaining the full-window privacy mask outside the reveal area
- Resolve shortcuts from focused tool windows and bound unresponsive window capture so unsupported apps retain a safe fallback without repeated capture work
- Migrate custom reveal behavior and shortcut defaults through settings schema version 9
- Add an application picker for visible desktop programs with executable icons, friendly names, process names, and window titles
- Allow browsing directly to an executable so an application can be configured before it is running
- Let custom profiles be renamed or removed while built-in WhatsApp and Telegram profiles remain protected from deletion
- Exclude PrivacyMask, duplicate processes, Windows shell surfaces, and security-sensitive processes from custom targeting
- Begin the user-defined application profile foundation for protecting desktop apps beyond WhatsApp and Telegram
- Give every profile a stable identity so multiple custom applications can coexist and receive independent hotkey updates
- Add safe custom-profile defaults, exact process-name matching, and a generic Windows adapter
- Preserve custom profiles and migrate existing 1.x profiles through settings schema version 8
- Add a lightweight wet-glass mask with transparent condensation droplets over live blur
- Expose mask-style selection in the primary per-application settings
- Preserve the selected mask style when window width activates another adaptive preset
- Add focused-app shortcuts to adjust blur strength by 5%
- Save shortcut adjustments atomically and prevent key-repeat from issuing duplicate steps
- Keep live capture running across blur-strength shortcut changes instead of getting stuck on the opaque fallback
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
