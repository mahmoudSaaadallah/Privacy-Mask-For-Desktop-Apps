# Architecture Overview

PrivacyMask is split into three projects:

- `PrivacyMask.Core`: models, contracts, defaults, preset selection, and settings persistence
- `PrivacyMask.Windows`: Win32 interop, startup registration, and desktop window discovery
- `PrivacyMask.App`: WPF tray shell, settings UI, onboarding, overlays, and hotkey orchestration

## Runtime flow

1. The tray application starts, loads local JSON settings, and initializes a transient protected runtime state.
2. The window inspector captures visible desktop windows.
3. Window adapters identify supported WhatsApp Desktop and Telegram Desktop windows.
4. The profile resolver selects a matching preset and effective mask settings.
5. The overlay manager positions click-through windows over the supported app windows.

See [runtime-protection.md](runtime-protection.md) for runtime state transitions,
panic-mask behavior, and persistence compatibility.

## Settings durability

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
