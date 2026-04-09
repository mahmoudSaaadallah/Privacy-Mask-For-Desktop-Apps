param(
  [string]$SourcePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop-app/windows/win-x64/app'),
  [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SourcePath)) {
  throw "Published app folder not found at '$SourcePath'. Run scripts/publish-win-x64.ps1 first."
}

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallPath -Recurse -Force

$exePath = Join-Path $InstallPath 'PrivacyMask.App.exe'
if (-not (Test-Path $exePath)) {
  throw "Could not find PrivacyMask.App.exe after copying files."
}

$shell = New-Object -ComObject WScript.Shell

$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyMask.lnk'
$desktopShortcut = $shell.CreateShortcut($desktopShortcutPath)
$desktopShortcut.TargetPath = $exePath
$desktopShortcut.WorkingDirectory = $InstallPath
$desktopShortcut.Description = 'PrivacyMask for Desktop Apps'
$desktopShortcut.Save()

$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PrivacyMask'
New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null
$startMenuShortcutPath = Join-Path $startMenuFolder 'PrivacyMask.lnk'
$startMenuShortcut = $shell.CreateShortcut($startMenuShortcutPath)
$startMenuShortcut.TargetPath = $exePath
$startMenuShortcut.WorkingDirectory = $InstallPath
$startMenuShortcut.Description = 'PrivacyMask for Desktop Apps'
$startMenuShortcut.Save()

Write-Host "Installed PrivacyMask to $InstallPath"
Write-Host "Desktop and Start Menu shortcuts were created."
