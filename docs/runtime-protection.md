# Runtime Protection States

PrivacyMask keeps protection state separate from persisted user preferences.
Closing the app while protection is paused or while the panic mask is active
does not make the next launch start unprotected.

## States

| State | Overlay behavior | Hover or hold reveal |
| --- | --- | --- |
| Protected | Uses the configured profile zones and mask appearance | Allowed when enabled by the zone and hotkey settings |
| Paused | Hides all PrivacyMask overlays | Disabled |
| Panic mask | Covers the complete bounds of every detected protected window with an opaque black mask | Disabled |

## Transitions

- PrivacyMask always starts in `Protected`.
- Toggle protection changes `Protected` to `Paused` and `Paused` to `Protected`.
- Toggle protection cannot bypass an active panic mask.
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

This separation also prevents repeated pause and panic hotkeys from creating
overlapping asynchronous settings writes.
