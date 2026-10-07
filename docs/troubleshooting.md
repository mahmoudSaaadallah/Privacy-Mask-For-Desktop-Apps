# Troubleshooting

## The mask does not appear

- Confirm that WhatsApp Desktop or Telegram Desktop is running.
- Make sure protection is not paused from the tray menu.
- Open the settings window and verify the supported app is enabled.

## The published build folder is missing

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

## Build fails because files are locked

Close any running `PrivacyMask.App.exe` process, then build again.

## The app opens only in the tray

If launch-at-sign-in is enabled, startup runs may use the `--minimized` argument. Open the settings window from the tray icon.

## A global shortcut is unavailable

Windows allows only one application to register a particular global shortcut.
PrivacyMask shows a tray warning and marks any failed shortcut in the settings
window. Close the other application using that shortcut, then save PrivacyMask
settings again to retry registration. Hold-to-reveal is detected directly and
does not use a global shortcut registration.

## Settings were recovered or reset

PrivacyMask keeps settings under
`%LocalAppData%\PrivacyMask.Desktop\settings.v1.json`. After an existing file
is successfully replaced, the previous version is retained as
`settings.v1.json.bak`.

If JSON parsing fails, PrivacyMask preserves the malformed file with a name
similar to `settings.v1.corrupt-20261007T120000000Z.json`, then restores the
backup. If the backup is missing or malformed, safe defaults are created. These
files contain PrivacyMask configuration only; they do not contain message
content.
