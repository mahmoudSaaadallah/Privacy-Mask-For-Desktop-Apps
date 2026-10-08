# PrivacyMask for Desktop Apps

PrivacyMask is a Windows desktop privacy companion for WhatsApp Desktop and Telegram Desktop. It places a local click-through mask over supported app windows so you can hide message content while still keeping the apps open on screen.

## Highlights

- Windows-first WPF tray application
- Supports WhatsApp Desktop and Telegram Desktop
- Local-only settings with no telemetry and no cloud dependency
- Single full-window mask for each supported app
- Hover reveal window around the mouse pointer
- Privacy-safe frosted-glass, pixelated, and solid-redact surfaces
- Adjustable surface intensity that remains fully opaque at every level
- Global hotkeys, tray controls, onboarding, and launch-at-sign-in support
- Adaptive window inspection and cached overlay rendering to reduce idle work

## Privacy model

- PrivacyMask does not read message content.
- PrivacyMask does not inject into WhatsApp or Telegram.
- PrivacyMask does not send data to a server.
- PrivacyMask does not capture the protected app to create its frosted effect;
  the mask is an opaque surface generated locally by PrivacyMask.
- All settings are stored locally in `%LocalAppData%\PrivacyMask.Desktop\settings.v1.json`.
- Settings saves are atomic and keep a local `.bak` recovery copy after the first update.

## End-user requirements

See [requirements.md](requirements.md) for the full list.

Minimum requirements:

- Windows 10 or Windows 11, 64-bit
- Official WhatsApp Desktop and/or Telegram Desktop

## End-user quick start

You do not need the source code or the .NET SDK to install a published release.

1. Open the [latest GitHub release](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases/latest).
2. Download the versioned Windows archive, for example `PrivacyMask-1.1.0-win-x64.zip`, and extract the whole archive.
3. Double-click `Install-PrivacyMask.cmd` in the extracted folder.
4. Launch PrivacyMask from the Desktop or Start Menu shortcut.
5. Complete onboarding, then open WhatsApp Desktop or Telegram Desktop.

The installer does not require administrator access. It copies the standalone app
to `%LocalAppData%\PrivacyMask.Desktop` and creates Desktop and Start Menu
shortcuts. To update, download the newer release and run
`Install-PrivacyMask.cmd` again. If PrivacyMask is running, the installer closes
it, preserves the settings, replaces the application transactionally, and
restarts it minimized. A failed update restores the previous installation.

To run the release without installing it, open `single-file` in the extracted
archive and double-click `PrivacyMask.App.exe`.

To uninstall, run `Uninstall-PrivacyMask.cmd` from the extracted release folder.
It removes the installed app, its shortcuts, and the settings stored with the
installation in `%LocalAppData%\PrivacyMask.Desktop`.

The matching `.sha256` release asset can be used to verify the downloaded ZIP:

```powershell
(Get-FileHash .\PrivacyMask-1.1.0-win-x64.zip -Algorithm SHA256).Hash
```

Compare the output with the hash in `PrivacyMask-1.1.0-win-x64.zip.sha256`.

## One-click install from source

If you cloned the repository and have the .NET 10 SDK installed, double-click
`Install-PrivacyMask.bat` in the repository root. It will:

1. Build the self-contained Windows executable.
2. Copy it to `%LocalAppData%\PrivacyMask.Desktop`.
3. Create Desktop and Start Menu shortcuts.

No administrator access is required. The first build can take a few minutes
while .NET restores the required packages. The same installer can be run again
to rebuild and update the local installation.

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

Published output is generated locally and intentionally ignored by Git.

Use the single-file publish script to create a double-clickable standalone executable:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64-single-file.ps1
```

Published output:

- `desktop-app/windows/win-x64/single-file`

Create the same installable ZIP and checksum used by GitHub Releases:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-win-x64-release.ps1
```

Release package output:

- `artifacts/release/PrivacyMask-1.1.0-win-x64.zip`
- `artifacts/release/PrivacyMask-1.1.0-win-x64.zip.sha256`

## Repository layout

- `src/PrivacyMask.Core`: core models, settings, presets, and profile resolution
- `src/PrivacyMask.Windows`: Windows interop, startup registration, and window discovery
- `src/PrivacyMask.App`: WPF app shell, settings UI, onboarding, overlay rendering, and hotkeys
- `tests/PrivacyMask.Core.Tests`: unit tests
- `docs`: architecture and release notes
- `scripts`: verification, publish, and local install scripts
- `desktop-app/windows/win-x64`: installer scripts and Windows distribution notes; generated app output is ignored

## Verification commands

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

## Performance measurements

Measure the resource usage of a running PrivacyMask process:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\measure-runtime.ps1
```

Compare the current published distributions with a temporary
framework-dependent publish:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\measure-publish-size.ps1
```

See [docs/performance-baseline.md](docs/performance-baseline.md) for the reference
environment, initial measurements, performance budgets, and required scenarios.

## Normal user guide

1. Start PrivacyMask.
2. Keep WhatsApp Desktop or Telegram Desktop open.
3. Move the surface intensity slider to your preferred appearance. Every level
   remains fully opaque; only the frost color, contrast, and texture change.
4. Hover over the masked app to reveal a small reading window around the pointer.
5. Use the tray icon to pause protection, apply an opaque panic mask, reopen settings, or exit the app.

Protection always starts enabled when PrivacyMask launches. Pausing protection
is a runtime-only action and is not restored after an app restart. The panic
mask covers every detected protected window in opaque black and disables reveal
gestures until panic mode is turned off.

If another program has already reserved a configured global shortcut,
PrivacyMask shows a tray warning and marks that shortcut as unavailable in the
settings window. Other registered shortcuts continue to work normally.

Mask appearance changes are previewed while the settings window is open. Press
`Ctrl+S` to save them, or press `Esc`/choose **Discard and close** to restore the
last saved configuration and return the window to the tray.

## Developer guide

- Review [docs/architecture.md](docs/architecture.md) for the project structure.
- Review [docs/release-process.md](docs/release-process.md) for the local release flow.
- Review [docs/troubleshooting.md](docs/troubleshooting.md) if builds or overlays are not behaving as expected.
- Review [docs/performance-baseline.md](docs/performance-baseline.md) before and after performance-sensitive changes.
- Review [docs/runtime-protection.md](docs/runtime-protection.md) for pause, panic-mask, and startup behavior.
- Review [docs/mask-rendering.md](docs/mask-rendering.md) for rendering guarantees, intensity behavior, and performance boundaries.
- Keep tests updated whenever profile matching, settings migration, or overlay behavior changes.

## Known limitations

- Overlay protection is designed for local privacy and best-effort full-screen sharing support.
- Window-only capture behavior depends on how external apps capture the desktop.
- Layout changes in WhatsApp Desktop or Telegram Desktop may require preset updates.
