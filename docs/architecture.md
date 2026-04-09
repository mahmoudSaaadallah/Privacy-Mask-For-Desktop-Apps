# Architecture Overview

PrivacyMask is split into three projects:

- `PrivacyMask.Core`: models, contracts, defaults, preset selection, and settings persistence
- `PrivacyMask.Windows`: Win32 interop, startup registration, and desktop window discovery
- `PrivacyMask.App`: WPF tray shell, settings UI, onboarding, overlays, and hotkey orchestration

## Runtime flow

1. The tray application starts and loads local JSON settings.
2. The window inspector captures visible desktop windows.
3. Window adapters identify supported WhatsApp Desktop and Telegram Desktop windows.
4. The profile resolver selects a matching preset and effective mask settings.
5. The overlay manager positions click-through windows over the supported app windows.

## Design boundaries

- No OCR
- No process injection
- No telemetry
- No cloud service dependency
