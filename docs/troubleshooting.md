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
