# PrivacyMask Windows Distribution

This folder contains the installer, uninstaller, and packaging notes. Generated
application files are intentionally not stored in Git.

## Install a GitHub release

1. Open the [latest PrivacyMask release](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases/latest).
2. Download the versioned Windows archive, for example `PrivacyMask-1.1.0-win-x64.zip`.
3. Extract the whole ZIP; do not run the installer from inside the archive.
4. Double-click `Install-PrivacyMask.cmd`.
5. Start PrivacyMask from the Desktop or Start Menu shortcut.

No administrator access is required. The installer copies the executable to
`%LocalAppData%\PrivacyMask.Desktop` and creates shortcuts for the current user.

## Portable use

After extracting the release ZIP, open `single-file` and double-click
`PrivacyMask.App.exe`. Keep the executable in a permanent location if you enable
launch at sign in.

## Update or uninstall

- To update, extract the newer release and run `Install-PrivacyMask.cmd` again.
  The installer preserves settings, removes stale application files, and rolls
  back to the previous installation if replacement or shortcut creation fails.
- To uninstall, run `Uninstall-PrivacyMask.cmd` from an extracted release package.

## Build and install from source

With the .NET 10 SDK installed, double-click `Install-PrivacyMask.bat` in the
repository root. It publishes and installs the application in one operation.
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
are ignored by Git.
Calling the `.ps1` directly installs the selected existing build without
rebuilding; use the root `.bat` or checkout `.cmd` for a normal source update.
