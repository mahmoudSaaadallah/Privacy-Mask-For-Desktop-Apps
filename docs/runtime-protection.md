# Runtime Protection States

PrivacyMask keeps protection state separate from persisted user preferences.
Closing the app while protection is paused or while the panic mask is active
does not make the next launch start unprotected.

## States

| State | Overlay behavior | Hover or hold reveal |
| --- | --- | --- |
| Protected | Uses the configured profile zones and mask appearance | Holding the reveal key alone for 1.5 seconds applies only to the focused profile; hover applies only to zones that explicitly enable it |
| Profile paused | Hides overlays belonging to one focused profile | Disabled for that profile |
| Globally paused | Hides all PrivacyMask overlays | Disabled |
| Panic mask | Covers the complete bounds of every detected protected window with an opaque black mask | Disabled |

## Transitions

- PrivacyMask always starts in `Protected`.
- `Ctrl + Shift + P` toggles only the focused protected profile.
- Hold reveal activates only after its key has remained down alone for 1.5
  seconds. Any other keyboard key cancels the current hold until the reveal key
  is released, preventing multi-key shortcuts from exposing content.
- The tray pause command changes `Protected` to `Globally paused` and back.
- Neither profile nor global pause can bypass an active panic mask.
- Toggle panic enters `Panic mask` from any non-panic state.
- Leaving `Panic mask` always returns to `Protected`, even when protection was paused before panic was activated.

The panic mask ignores disabled zones and focus-aware activation for profiles
that are enabled. It still respects unrelated windows above the protected app so
that PrivacyMask does not cover other desktop applications.

## Persistence compatibility

Settings schema version 7 retains the legacy `currentMode` JSON field so older
files remain readable. The field is normalized to the protected value whenever
settings are loaded or saved. New runtime transitions do not write the settings
file.

Version 7 also migrates the old 0.15–2.40 mask-darkness values to the
0.00–1.00 blur-strength scale while preserving their relative visual position.
Existing numeric `Blur` enum values remain compatible and are presented as
`Live blur` after migration.

Profile pauses are held in memory by stable profile identifier, so pausing one
application does not reveal another. Removed profiles are pruned from this
runtime state. This separation also prevents repeated pause and panic hotkeys
from creating overlapping asynchronous settings writes.
