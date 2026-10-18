# PrivacyMask Windows Distribution

This folder contains the installer, uninstaller, and packaging notes. Generated
application files are intentionally not stored in Git.

## Install a GitHub release

1. Open [PrivacyMask releases](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases). **Pre-release** builds are for testing; `latest` selects stable releases.
2. Download the archive matching your chosen release, for example `PrivacyMask-2.0.0-beta.9-win-x64.zip`.
3. Extract the whole ZIP; do not run the installer from inside the archive.
4. Double-click `Install-PrivacyMask.cmd`.
5. Start PrivacyMask from the Desktop or Start Menu shortcut.

No administrator access is required. The installer creates the managed layout
under `%LocalAppData%\PrivacyMask.Desktop`, keeps the stable launcher at its root,
places the replaceable App in `app`, and creates current-user shortcuts that
target the launcher.
The source version does not mean a matching release has already been published.
About shows the product version, including beta/rc, and its version tooltip
shows full build metadata. Windows file versions remain numeric.

## Portable use

After extracting the release ZIP, open `single-file` and double-click
`PrivacyMask.App.exe`. Keep the executable in a permanent location if you enable
launch at sign in.

## Update or uninstall

- To update, extract the newer release and run `Install-PrivacyMask.cmd` again.
  The installer preserves settings and the installation identity, removes stale
  application files, and rolls back to the previous installation if replacement,
  shortcut or startup migration fails. Beta.8 adds Settings/tray manual checks,
  but signed discovery reports "not configured" without approved compiled keys.
  Use View releases for manual installation. Background checks and in-app
  download/apply are not activated yet.
  Beta.9 implements bounded startup scheduling, but creates no timer/request
  without approved signing keys. Manual reinstall preserves its bounded retry
  history; unknown pending history is not automatically reset.
- To uninstall, run `Uninstall-PrivacyMask.cmd` from an extracted release package.

## Build and install from source

With the .NET 10 SDK and Visual Studio Windows C++ build workload installed,
double-click `Install-PrivacyMask.bat` in the repository root. It publishes the
App plus Native-AOT launcher and installs them in one operation.
Exit an already running portable/repository copy from its tray before updating.
After `git pull`, this rebuild is required: generated executables are not tracked.
The distribution-folder `.cmd` also rebuilds when all source checkout markers
are present, even if an older executable exists. Release archives do not contain
those markers and continue to install without the SDK.

To perform the same steps manually, first publish the standalone executable:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64-single-file.ps1
```

Then run the local installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\desktop-app\windows\win-x64\Install-PrivacyMask.ps1
```

The generated `single-file` and `app` directories are local build output and
are ignored by Git. Only the complete `single-file` payload is accepted by the
managed installer; use `app` directly as portable developer output.
Calling the `.ps1` directly installs the selected existing build without
rebuilding; use the root `.bat` or checkout `.cmd` for a normal source update.
