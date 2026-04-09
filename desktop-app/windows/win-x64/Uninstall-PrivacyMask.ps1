$ErrorActionPreference = 'Stop'

$installPath = Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop'
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyMask.lnk'
$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PrivacyMask'

if (Test-Path $installPath) {
  Remove-Item -LiteralPath $installPath -Recurse -Force
}

if (Test-Path $desktopShortcutPath) {
  Remove-Item -LiteralPath $desktopShortcutPath -Force
}

if (Test-Path $startMenuFolder) {
  Remove-Item -LiteralPath $startMenuFolder -Recurse -Force
}

Write-Host "PrivacyMask was removed from the local profile."
