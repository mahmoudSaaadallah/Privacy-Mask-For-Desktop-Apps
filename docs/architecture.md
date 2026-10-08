# Architecture Overview

PrivacyMask is split into three projects:

- `PrivacyMask.Core`: models, contracts, defaults, preset selection, and settings persistence
- `PrivacyMask.Windows`: Win32 interop, startup registration, and desktop window discovery
- `PrivacyMask.App`: WPF tray shell, settings UI, onboarding, overlays, and hotkey orchestration

## Runtime flow

1. The tray application starts, loads local JSON settings, and initializes a transient protected runtime state.
2. The window inspector captures visible desktop windows.
3. Built-in adapters identify WhatsApp and Telegram windows, while a generic
   adapter exactly matches processes selected for user-defined profiles.
4. The profile resolver selects a matching preset and effective mask settings.
5. The overlay manager positions click-through windows over the supported app windows.

The discovery pass keeps lightweight bounds and z-order records for every
visible desktop window so occlusion remains correct. Process names are cached,
and title/class metadata plus full window models are created only for processes
that can match an enabled profile. Effective mask zones are reused until their
source configuration changes.

The refresh cadence is 500 ms while idle or paused and 100 ms while candidate
windows exist. Pause and panic transitions request an immediate refresh, so the
idle cadence does not delay protection state changes. Each overlay compares its
last rendered bounds, profile, zones, occlusion list, reveal state, and relevant
cursor position; unchanged frames do not rebuild WPF brushes or geometry.

See [runtime-protection.md](runtime-protection.md) for runtime state transitions,
panic-mask behavior, and persistence compatibility.

Mask appearance is resolved independently from overlay geometry. Live blur
captures the target window handle directly into a bounded in-memory frame,
downsamples it off the UI thread, and reuses the existing WPF image elements
between refreshes. Pixelate, solid redact, capture fallback, and panic mode use
local WPF drawing resources. See [mask-rendering.md](mask-rendering.md) for the
rendering behavior and blur-strength scale.

Wet glass shares the same bounded capture path and adds one frozen transparent
texture generated at process startup. The texture is shared by every overlay,
contains no external asset, and is composited at a lower two-frame-per-second
capture cadence to limit resource use.

## Settings durability

- Opening the settings window starts an edit session from a deep snapshot of
  the last saved configuration. Visual mask changes can be previewed live, but
  dismissing the window restores that snapshot and refreshes overlays.
- Saving atomically promotes the edited configuration to the new snapshot.
  Re-activating an already visible settings window preserves in-progress edits
  instead of rebuilding its view model.
- Settings operations are serialized so overlapping UI and tray saves cannot
  write the same file concurrently.
- A save is written to a temporary file in the settings directory and then
  atomically replaces the primary JSON file.
- Replacing an existing file keeps the previous valid file as
  `settings.v1.json.bak`.
- If the primary JSON is malformed, it is renamed with a UTC
  `.corrupt-<timestamp>` suffix before the backup is restored. Defaults are
  used only when neither the primary file nor the backup can be parsed.
- A normalized file is not rewritten during startup when its serialized form
  is already current, reducing unnecessary disk writes.
- Settings schema version 8 assigns every profile a stable identifier. Legacy
  1.x WhatsApp and Telegram settings receive their built-in identifiers during
  normalization, while user-defined profiles retain their own identities and
  survive save/reload cycles.

## Lifecycle and hotkeys

Shell shutdown is idempotent and explicitly releases the refresh timer,
registered hotkeys, overlay windows, settings window, and tray icon. Global
hotkey registration returns per-binding failures so a conflict can be shown in
the tray and settings UI without disabling shortcuts that registered
successfully.

## Design boundaries

- No OCR
- No process injection
- No telemetry
- No cloud service dependency
- No captured-frame persistence or transmission
