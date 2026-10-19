# PrivacyMask for Desktop Apps

PrivacyMask is a Windows desktop privacy companion for WhatsApp Desktop, Telegram Desktop, and other applications you choose. It places a local click-through mask over protected app windows so you can hide sensitive content while still keeping the apps open on screen.

The current source targets `2.0.0-beta.15`, Preview B preparation, not a declaration
that a stable 2.0.0 release has been published. Check the releases page for
available downloads. About and Settings show the product version; hover the
About version for full build/source metadata and the numeric Windows file version.

## Highlights

- Windows-first WPF tray application
- Includes ready-made profiles for WhatsApp Desktop and Telegram Desktop
- Adds any running desktop application, or an application executable selected from disk
- Local-only settings with no telemetry and no cloud dependency
- Single full-window mask for each supported app
- Hover reveal window around the mouse pointer
- Live blur, wet-glass, pixelated, and solid-redact surfaces
- Adjustable blur strength that progressively reveals or hides visual detail
- Global hotkeys, tray controls, onboarding, and launch-at-sign-in support
- Adaptive window inspection and cached overlay rendering to reduce idle work

## Privacy model

- PrivacyMask does not read message content.
- PrivacyMask does not inject into protected applications.
- Profiles, PINs, executable paths, window titles and captures are never sent to a server.
- Beta.14 enables required signed automatic updates for managed Windows 11 x64
  Preview installations. Successful checks repeat every **10 minutes in beta.14
  only**; beta.15 and later default back to 24 hours. These requests contact
  GitHub and expose normal connection metadata such as your IP address, never
  profiles, PINs, paths, window titles or captures. Stable activation remains
  disabled; source/portable launches and unsupported installations do not start
  automatic checks. View releases opens your browser only when requested.
- Live blur and wet glass capture only the selected application window into a bounded
  in-memory frame. Frames are never written to disk or sent anywhere, and are
  released when the mask is hidden or the application exits.
- All settings are stored locally in `%LocalAppData%\PrivacyMask.Desktop\settings.v1.json`.
- Settings saves are atomic and keep a local `.bak` recovery copy after the first update.

## End-user requirements

See [requirements.md](requirements.md) for the full list.

Minimum requirements:

- Windows 10 or Windows 11, 64-bit
- At least one conventional Windows desktop application to protect

## End-user quick start

You do not need the source code or the .NET SDK to install a published release.

1. Open [GitHub releases](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases). Releases marked **Pre-release** are testing builds; the `latest` link is for stable releases.
2. Download the archive matching the release you selected, for example `PrivacyMask-2.0.0-beta.15-win-x64.zip` once beta.15 is published, and extract the whole archive. Use beta.14 for the A baseline until then.
3. Double-click `Install-PrivacyMask.cmd` in the extracted folder.
4. Launch PrivacyMask from the Desktop or Start Menu shortcut.
5. Complete onboarding. Use **Add application** to select a running program or browse to its `.exe` file.

The installer does not require administrator access. It places the stable
launcher at `%LocalAppData%\PrivacyMask.Desktop\PrivacyMask.Launcher.exe`, the
replaceable App under `app`, and creates Desktop and Start Menu shortcuts that
target the launcher. To update, download the newer release and run
`Install-PrivacyMask.cmd` again. If PrivacyMask is running, the installer closes
it, preserves the settings, replaces the application transactionally, and
restarts it minimized. A failed update restores the previous installation.
Settings > Updates and the tray include **Check for updates** and **View releases**.
Beta.14 is the owner-approved [Preview A-to-B trial](docs/update-preview-trial.md),
not stable qualification. Install the complete A installer once; older disabled
builds such as beta.13 cannot activate themselves remotely. No signing key, Git,
.NET SDK or PowerShell 7 is needed on the end user's device.
The [required-update policy](docs/update-mandatory.md) checks at startup/reconnect
and every **10 minutes after a successful check in beta.14**, downloads newer
compatible signed releases automatically and installs without confirmation.
The exact-version trial exception expires automatically in beta.15 and later,
which use 24 hours after success with the same saved history and settings.
There is no in-app opt-out, download cancellation or discard. Stable builds follow
Stable; testing builds follow Preview. Existing saved opt-out/channel choices
cannot disable this required policy. Failed checks retry no sooner than 15 minutes;
persisted GitHub retry deadlines survive restarts and always take precedence.

Installation waits automatically while Panic is locked, settings edits are
unsaved/invalid, or protected application windows remain visible. Protection stays
active while waiting; normal PIN/Save/Discard actions resolve those conditions.
No PIN is bypassed and edits are never silently discarded. Close or minimize
protected windows to allow the automatic restart. Desktop checks are best effort,
not an OS lock against another program restoring a window during restart. The
policy cannot prevent terminating/blocking/uninstalling PrivacyMask in Windows,
and cannot retroactively update older binaries that lack an active updater.

To run the release without installing it, open `single-file` in the extracted
archive and double-click `PrivacyMask.App.exe`. That is **portable use** and does
not support automatic updates, even when a managed copy is also installed.
For automatic updates, exit the portable copy from its tray and use the installed
Desktop or Start Menu shortcut. A version label alone does not identify which
copy is running.

To uninstall, run `Uninstall-PrivacyMask.cmd` from the extracted release folder.
It removes the installed app, its shortcuts, and the settings stored with the
installation in `%LocalAppData%\PrivacyMask.Desktop`.

The matching `.sha256` release asset can be used to verify the downloaded ZIP:

```powershell
(Get-FileHash .\PrivacyMask-2.0.0-beta.15-win-x64.zip -Algorithm SHA256).Hash
```

Compare the output with the hash in `PrivacyMask-2.0.0-beta.15-win-x64.zip.sha256`.
Use the actual versioned filenames if you downloaded another release.

## One-click install from source

If you cloned the repository and have the .NET 10 SDK plus the Visual Studio
Windows C++ build workload installed, double-click `Install-PrivacyMask.bat` in
the repository root. It will:

1. Build the self-contained Windows executable.
2. Copy it to `%LocalAppData%\PrivacyMask.Desktop`.
3. Create Desktop and Start Menu shortcuts.

No administrator access is required. The first build can take a few minutes
while .NET restores the required packages. The same installer can be run again
to rebuild and update the local installation.

In a complete source checkout, `desktop-app/windows/win-x64/Install-PrivacyMask.cmd`
also routes through the root batch installer and rebuilds, even when an older
published executable already exists. A `git pull` updates source files, not
ignored build output. Extracted release archives still install their packaged
executable directly, without requiring the SDK.

Before updating from source, exit PrivacyMask from its tray menu, especially
if it was launched from the repository's `single-file` folder. Otherwise a
portable instance can keep its executable locked or cause a newly launched
copy to reactivate the older instance. Reopen the installed Desktop/Start Menu
shortcut after a successful build/install.

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

- `artifacts/release/PrivacyMask-2.0.0-beta.15-win-x64.zip`
- `artifacts/release/PrivacyMask-2.0.0-beta.15-win-x64.zip.sha256`

## Repository layout

- `src/PrivacyMask.Core`: core models, settings, presets, and profile resolution
- `src/PrivacyMask.Windows`: Windows interop, startup registration, and window discovery
- `src/PrivacyMask.App`: WPF app shell, settings UI, onboarding, overlay rendering, and hotkeys
- `src/PrivacyMask.Launcher`: stable native managed-install start and crash-recovery entry point
- `src/PrivacyMask.Updates`: update trust, discovery, recovery transaction and authenticated app-start contracts; runtime checks fail closed until signing is approved
- `tests/PrivacyMask.Core.Tests`: unit tests
- `docs`: architecture and release notes
- `scripts`: verification, publish, and local install scripts
- `desktop-app/windows/win-x64`: installer scripts and Windows distribution notes; generated app output is ignored

## Verification commands

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

Verification includes isolated compiled WPF layout and edit-session smoke
checks; it does not start the production privacy runtime or change your settings.
Run just the GUI checks with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-gui-smoke.ps1
```

See [GUI qualification](docs/gui-qualification.md) for the current evidence,
resource comparison, limitations, and remaining live release checks.

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
2. Select **Add application**, then choose a visible running program or browse
   directly to its `.exe` file. WhatsApp and Telegram are already included.
3. On **Applications**, search by application or process name and select **Manage**
   on a card to edit that profile. Rename a custom profile if needed, then choose a mask style. `Live blur` keeps the cleanest appearance, while
   `Wet glass` adds a locally generated condensation layer.
4. Move the blur-strength slider to your preferred appearance. Lower values
   reveal progressively clearer shapes and colors; higher values remove more
   detail. Use solid redact or panic mode when no underlying detail may remain.
5. While a protected application is focused, press `Ctrl + Alt + Shift + ↑` to
   raise its saved blur strength by 5%, or `Ctrl + Alt + Shift + ↓` to lower it
   by 5%.
6. Focus any normal desktop application and press `Ctrl + Shift + P`. If the
   application has not been configured yet, PrivacyMask adds it with all custom
   profile defaults, saves it, and activates its mask immediately. Repeating
   the shortcut pauses or resumes that application's existing profile.
7. Hold `Alt` by itself for 0.5 seconds to reveal only the focused protected
   application. Pressing any other key during that hold cancels reveal until
   `Alt` is released, so blur shortcuts never expose the app. Press `Ctrl + Shift + H`
   to toggle an opaque panic mask for only the focused application. The first
   panic action asks you to create a 4–8 digit unlock
   PIN; disabling a focused or global panic mask always requires that PIN.
   Mouse-hover reveal is enabled for built-in and custom applications by
   default, using a 600×70 pixel reveal window.
8. Use the tray icon to pause every profile, apply an opaque panic mask, reopen
   settings, or exit the app.

Normal tray Exit waits for already accepted settings actions before removing
protection and closing. While a Save is pending, closing/discarding the editor
cannot interrupt it. If another action is still finishing, Save asks you to
review your changes and retry rather than overwrite a newer saved value. This
does not make forced termination, sign-out or installer-forced exit graceful.

Each profile card reports whether its application is protected now, waiting for
focus, paused, disabled, not running, or no longer detected at its saved path.
The information button provides contextual help on hover. Focusing it alone
does not open a popup. Overview and Security show visual information popups
only on hover; their accessible help text remains available to screen readers.
On other pages, focus the information button and press `F1`, `Enter`, or
`Space` to open it explicitly; `Esc` dismisses focused open help.
Profile details contain the primary appearance and hover controls. Open
**Advanced privacy zones** for the full-width preset and zone editor. **Back**
returns to the same profile without losing edits; Save/Discard uses the shared
settings session. Coordinates remain relative fractions (0–1), not fixed pixels,
so zones adapt to the application's changing window size.
Search in the application picker narrows running candidates; check the selected
executable path before confirming. Browsing to an executable remains available.
For a custom application that moved or changed its executable after an update,
choose **Relink** and select the current running application or `.exe` file.
Relinking changes only the application target; the profile name, blur strength,
mask style, zones, hover reveal dimensions, and other privacy settings remain
unchanged until you save or discard the edit.

Protection always starts enabled when PrivacyMask launches. Both a focused-app
pause, focused-app panic, and global tray controls are runtime-only actions and
are not restored after an app restart. A focused panic mask covers only that
application; **Panic mask all** from the tray covers every detected protected
window. Both panic modes disable reveal for the windows they cover.
New installations also enable launch at sign-in by default; this can be changed
from onboarding, settings, or the tray without affecting existing users' saved
choice during an update.

The panic PIN can be added or changed from **Security > Panic unlock PIN**.
When editing it in Security, confirm the dialog and then use **Save changes**
to persist the new PIN. First-use panic setup saves its PIN before protection
is activated. Canceling a setup or change dialog keeps the current credential.
Changing it requires the current PIN. PrivacyMask stores only a salted,
computationally hardened verifier in the local settings file, never the digits
you entered. Canceling the unlock window or entering an incorrect PIN leaves
the panic mask active.

PIN fields are masked by default. Each eye button explicitly shows or hides
only that field. Hiding it clears the plaintext display; closing the dialog
clears all PIN inputs. Incorrect-PIN feedback remains beside the input and
visible even when the fields need to scroll. Unlock removes the panic mask;
normal privacy settings still apply, rather than promising a timed reveal.

If another program has already reserved a configured global shortcut,
PrivacyMask shows a tray warning and marks that shortcut as unavailable in the
settings window. Other registered shortcuts continue to work normally.

Use the sidebar to navigate between Overview, Applications, Shortcuts,
Security, Settings, and About. Pending edits stay available when you change
pages. Valid mask changes are previewed while the settings window is open.
Overview reports configured/enabled profiles and applications protected now.
It shows quick blur sliders for up to six profiles; **Manage all applications**
opens the complete searchable list. Slider edits use the same Save/Discard
session as profile details. **Pause all / Resume all** and **Panic mask all**
are immediate global runtime actions, just like the tray controls; Discard does
not undo them. Unlocking a global panic mask still requires the saved PIN.
The Shortcuts page explains focused-app scope, the 0.5-second reveal delay,
registration conflicts, and pending enable/disable changes. It does not remap
gestures. Close a conflicting program and restart PrivacyMask to retry an
unavailable shortcut. Settings contains startup preferences and an
**Open settings folder** action; the displayed local file path can be copied.
In the compact Settings layout this action is **Show location**. The adjacent
information button shows the full path; right-click or press `Shift+F10` on
**Show location** and choose **Copy file path** to copy it. The **About** card
opens the existing About sidebar page.
Shortcuts uses compact keycaps and switches; information buttons contain scope
and registration details, while conflicts and pending changes remain visible.
Security's **Active app / All apps** tabs explain the existing panic controls;
selecting a tab does not activate a mask or change shortcut scope.
About now shows the product version, developer details, privacy commitment,
and capture limitations directly in the sidebar page. Bugs and suggestions
can be sent to `mahmoud.saadallah73@gmail.com` using **Send email**, or
the address can be copied with the copy button beside it. GitHub and LinkedIn
profile rows and their external-link buttons open the same public profiles.
Every sidebar page uses the same shield/PM/PrivacyMask header as About.
About uses two columns when there is enough room and stacks its cards on
narrower windows. Extended capture/blur limitations are in the information
button beside the visible window-sharing warning. The shield is page branding;
the previously approved executable and tray icon stays unchanged.
Press `Ctrl+S` or **Save changes** to save the complete session, or **Discard**
to restore saved values across all pages; both keep the selected page open.
Press `Esc` or close the window to discard the preview and return to the tray.

## Developer guide

- Review [docs/architecture.md](docs/architecture.md) for the project structure.
- Review [docs/release-process.md](docs/release-process.md) for the local release flow.
- Follow its beta/rc/stable version policy before the next shipped-app delivery; PRs and commits are not themselves GitHub releases.
- Review [the in-app update delivery plan](docs/in-app-updates.md),
  [signed manifest contract](docs/update-manifest.md), and
  [recovery/threat model](docs/update-recovery.md) for the upcoming update system.
  Phase 3 adds a [read-only discovery library](docs/update-discovery.md) with
  offline HTTP tests, alongside signed-package tooling and a verified draft publisher;
  beta.8 now exposes [manual Settings/tray discovery](docs/update-settings-ui.md),
  gated by compiled signing trust. Keep using the installation procedure above.
- Internal phase-5 work now includes the recovery kernel, measured bootstrap
  prototype and [authenticated preparation](docs/update-preparation.md). The
  internal
  [transaction worker](docs/update-transaction.md) is tested with signed disposable
  processes; authenticated apply is not connected to the updater UI yet.
- The [authenticated App readiness foundation](docs/update-app-readiness.md) now
  validates update-start identity, migrates the exact saved settings baseline and
  receipts a durable commit after shell initialization. The
  [stable launcher and managed installer](docs/update-stable-launcher.md) make
  startup/recovery reachable in beta.5. The internal
  [authenticated handoff contract](docs/update-handoff.md) now binds the App and
  launcher to one exact update operation. The beta.6
  [runtime handoff](docs/update-runtime-handoff.md) connects restart consent,
  panic/edit checks, writer suspension and the launcher apply entrypoint.
  The beta.7 [storage maintenance](docs/update-retention.md) retains a verified
  recovery pair, prunes obsolete terminal history/expired staging, and bounds
  admission around unresolved evidence. The beta.8 Settings/tray UI adds manual
  checks and schema-13 preferences. Beta.9 adds [durable startup discovery](docs/update-check-scheduling.md)
  with bounded history, opt-out and persistent retry admission. Beta.10 connects
  [explicit download/progress/cancel/discard](docs/update-download-ui.md) through
  the same session. Beta.11 connects [owned-stage Update now](docs/update-install-ui.md)
  through consent/Panic/edit/handoff gates, with shared durable retry in the App
  and native launcher. Beta.12 [pins the owner's public key](docs/update-public-trust.md)
  in both consumers while retaining a separate disabled activation gate. Network
  activation and live qualification remain pending;
  normal Exit is not update consent or authorization.
- Beta.13 adds [required automatic update orchestration](docs/update-mandatory.md)
  with fixed build-channel policy, startup/reconnect/daily scheduling, bounded
  retries and automatic safe apply. It keeps runtime activation disabled and
  does not configure secrets/environments or publish a release.
- Review [update signing operations](docs/update-signing-operations.md) before
  publishing: the Windows baseline/environment remain intentionally unconfigured,
  so signed release publication fails closed until explicitly approved setup.
- Follow the [signing readiness plan](docs/update-signing-readiness.md) for the
  approved SoloMaintainer review model and separate custody/setup/publication
  gates. The [isolated update lab runbook](docs/update-qualification-runbook.md)
  prepares Windows 10/11 evidence; `scripts/prepare-update-qualification.ps1`
  creates only a fresh local `NotRun` packet, not a VM or completed test report.
- Run `pwsh -File scripts/test-update-manifest-schema.ps1` to verify the proposed
  update schemas. PowerShell 7 is required for this developer check only;
  end users do not need it. CI and release verification also run this check.
- Review [docs/troubleshooting.md](docs/troubleshooting.md) if builds or overlays are not behaving as expected.
- Review [docs/performance-baseline.md](docs/performance-baseline.md) before and after performance-sensitive changes.
- Review [docs/runtime-protection.md](docs/runtime-protection.md) for pause, panic-mask, and startup behavior.
- Review [docs/mask-rendering.md](docs/mask-rendering.md) for rendering guarantees, intensity behavior, and performance boundaries.
- Keep tests updated whenever profile matching, settings migration, or overlay behavior changes.

## Known limitations

- Overlay protection is designed for local privacy and best-effort full-screen sharing support.
- Window-only capture behavior depends on how external apps capture the desktop.
- Layout changes in WhatsApp Desktop or Telegram Desktop may require preset updates.
- Custom profiles match a process name and therefore protect every visible
  top-level window created by that process. Windows shell and security-sensitive
  processes are intentionally unavailable in the picker.
