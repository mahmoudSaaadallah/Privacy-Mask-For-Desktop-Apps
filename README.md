# PrivacyMask for Desktop Apps

PrivacyMask is a Windows desktop privacy companion for WhatsApp Desktop and Telegram Desktop. It places a local click-through mask over supported app windows so you can hide message content while still keeping the apps open on screen.

## Highlights

- Windows-first WPF tray application
- Supports WhatsApp Desktop and Telegram Desktop
- Local-only settings with no telemetry and no cloud dependency
- Single full-window mask for each supported app
- Hover reveal window around the mouse pointer
- Adjustable mask darkness
- Global hotkeys, tray controls, onboarding, and launch-at-sign-in support

## Privacy model

- PrivacyMask does not read message content.
- PrivacyMask does not inject into WhatsApp or Telegram.
- PrivacyMask does not send data to a server.
- All settings are stored locally in `%LocalAppData%\PrivacyMask.Desktop\settings.v1.json`.

## End-user requirements

See [requirements.md](requirements.md) for the full list.

Minimum requirements:

- Windows 10 or Windows 11, 64-bit
- Official WhatsApp Desktop and/or Telegram Desktop

## End-user quick start

If you only want to use the desktop app and do not want to run the source code directly:

1. Open [desktop-app/windows/win-x64/README.md](desktop-app/windows/win-x64/README.md).
2. Go to `desktop-app/windows/win-x64/single-file` after the project has been published.
3. Double-click `PrivacyMask.App.exe`.
4. Complete onboarding and choose whether the app should launch at sign in.
5. Open WhatsApp Desktop or Telegram Desktop and confirm the overlay appears.

Optional local install with shortcuts:

```powershell
.\desktop-app\windows\win-x64\Install-PrivacyMask.cmd
```

## Developer setup

1. Install the tools listed in [requirements.md](requirements.md).
2. Restore the solution:

```powershell
dotnet restore PrivacyMask.Desktop.slnx
```

3. Build the solution:

```powershell
dotnet build PrivacyMask.Desktop.slnx
```

4. Run tests:

```powershell
dotnet test PrivacyMask.Desktop.slnx
```

5. Run the app from source:

```powershell
dotnet run --project src/PrivacyMask.App/PrivacyMask.App.csproj
```

## Publish a Windows desktop build

Use the publish script to create a self-contained Windows desktop build:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

Published output:

- `desktop-app/windows/win-x64/app`

Use the single-file publish script to create a double-clickable standalone executable:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64-single-file.ps1
```

Published output:

- `desktop-app/windows/win-x64/single-file`

## Repository layout

- `src/PrivacyMask.Core`: core models, settings, presets, and profile resolution
- `src/PrivacyMask.Windows`: Windows interop, startup registration, and window discovery
- `src/PrivacyMask.App`: WPF app shell, settings UI, onboarding, overlay rendering, and hotkeys
- `tests/PrivacyMask.Core.Tests`: unit tests
- `docs`: architecture and release notes
- `scripts`: verification, publish, and local install scripts
- `desktop-app/windows/win-x64`: published desktop app output and end-user notes

## Verification commands

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

## Normal user guide

1. Start PrivacyMask.
2. Keep WhatsApp Desktop or Telegram Desktop open.
3. Move the mask darkness slider to your preferred level.
4. Hover over the masked app to reveal a small reading window around the pointer.
5. Use the tray icon to pause protection, reopen settings, or exit the app.

## Developer guide

- Review [docs/architecture.md](docs/architecture.md) for the project structure.
- Review [docs/release-process.md](docs/release-process.md) for the local release flow.
- Review [docs/troubleshooting.md](docs/troubleshooting.md) if builds or overlays are not behaving as expected.
- Keep tests updated whenever profile matching, settings migration, or overlay behavior changes.

## Known limitations

- Overlay protection is designed for local privacy and best-effort full-screen sharing support.
- Window-only capture behavior depends on how external apps capture the desktop.
- Layout changes in WhatsApp Desktop or Telegram Desktop may require preset updates.
