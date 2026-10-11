# Runtime Protection States

PrivacyMask keeps protection state separate from persisted user preferences.
Closing the app while protection is paused or while the panic mask is active
does not make the next launch start unprotected.

## States

| State | Overlay behavior | Hover or hold reveal |
| --- | --- | --- |
| Protected | Uses the configured profile zones and mask appearance | Holding the reveal key alone for 0.5 seconds applies only to the focused profile; hover applies only to zones that explicitly enable it |
| Profile paused | Hides overlays belonging to one focused profile | Disabled for that profile |
| Globally paused | Hides all PrivacyMask overlays | Disabled |
| Profile panic mask | Covers every detected window belonging to the focused profile with an opaque black mask | Disabled for that profile |
| Global panic mask | Covers every detected protected window with an opaque black mask | Disabled |

## Transitions

- PrivacyMask always starts in `Protected`.
- `Ctrl + Shift + P` toggles only the focused protected profile. When the
  foreground application has no profile, the same command creates and saves a
  custom profile with the complete default mask configuration, then activates
  it immediately. A matching disabled profile is re-enabled instead of being
  duplicated.
- `Ctrl + Shift + H` toggles an opaque panic mask only for the focused protected
  profile. Other profiles keep their normal protection appearance. Enabling it
  does not require an unlock, but disabling it requires the configured PIN.
- Hold reveal activates only after its key has remained down alone for 0.5
  seconds. Any other keyboard key cancels the current hold until the reveal key
  is released, preventing multi-key shortcuts from exposing content.
- The tray pause command changes `Protected` to `Globally paused` and back.
- A profile pause cannot bypass a panic mask for that profile.
- The tray **Panic mask all** command enters `Global panic mask` from any
  non-global-panic state and overrides all profile states.
- Leaving global panic requires the configured PIN and then returns to
  `Protected`; any profile-specific panic or pause state then becomes effective
  again.

The first panic action asks the user to create a 4–8 digit PIN and saves its
credential before entering panic mode. Canceling setup cancels that panic
action. An incorrect PIN or a canceled unlock dialog never changes the current
panic state. The PIN can be changed from settings only after verifying the
current PIN.

Both panic masks ignore disabled zones and focus-aware activation for the
profiles they cover. They still respect unrelated windows above the protected
app so that PrivacyMask does not cover other desktop applications.

Automatic foreground-app enrollment uses the same eligibility policy as the
application picker. PrivacyMask itself, Windows shell surfaces, security
processes, and an already configured process cannot be added as a new custom
profile.

While settings are visible, PrivacyMask reports each profile as protected,
panic-protected, paused, waiting for focus, disabled, not running, or missing
its saved executable. A custom profile can be relinked to a running application
or executable without replacing its identity or mask configuration. The relink
is previewed immediately and remains undo-friendly until settings are saved.

## Persistence compatibility

Settings schema version 7 retains the legacy `currentMode` JSON field so older
files remain readable. The field is normalized to the protected value whenever
settings are loaded or saved. New runtime transitions do not write the settings
file.

Version 7 also migrates the old 0.15–2.40 mask-darkness values to the
0.00–1.00 blur-strength scale while preserving their relative visual position.
Existing numeric `Blur` enum values remain compatible and are presented as
`Live blur` after migration.

Settings schema version 11 enables hover reveal on existing custom-application
zones and presets. It also adds an optional panic credential containing a
random salt, PBKDF2-SHA256 iteration count, and derived hash. The plaintext PIN
is never serialized.

Profile pauses and panic masks are held in memory by stable profile identifier,
so changing one application does not affect another. Removed profiles are
pruned from this runtime state. This separation also prevents repeated pause
and panic hotkeys from creating overlapping asynchronous settings writes.
