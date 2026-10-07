# PrivacyMask Windows Distribution

This folder contains the installer, uninstaller, and packaging notes. Generated
application files are intentionally not stored in Git.

## Install a GitHub release

1. Open the [latest PrivacyMask release](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases/latest).
2. Download `PrivacyMask-win-x64.zip`.
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
- To uninstall, run `Uninstall-PrivacyMask.cmd` from an extracted release package.

## Build and install from source

With the .NET 10 SDK installed, double-click `Install-PrivacyMask.bat` in the
repository root. It publishes and installs the application in one operation.

To perform the same steps manually, first publish the standalone executable:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64-single-file.ps1
```

Then run the local installer:

```powershell
.\desktop-app\windows\win-x64\Install-PrivacyMask.cmd
```

The generated `single-file` and `app` directories are local build output and
are ignored by Git.
