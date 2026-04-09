$ErrorActionPreference = 'Stop'

$buildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $buildRoot 'app'
$installPath = Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop'

if (-not (Test-Path $sourcePath)) {
  throw "Published app folder not found at '$sourcePath'."
}

New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Copy-Item -Path (Join-Path $sourcePath '*') -Destination $installPath -Recurse -Force

$exePath = Join-Path $installPath 'PrivacyMask.App.exe'
$shell = New-Object -ComObject WScript.Shell

$desktopShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyMask.lnk'))
$desktopShortcut.TargetPath = $exePath
$desktopShortcut.WorkingDirectory = $installPath
$desktopShortcut.Description = 'PrivacyMask for Desktop Apps'
$desktopShortcut.Save()

$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PrivacyMask'
New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null
$startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuFolder 'PrivacyMask.lnk'))
$startMenuShortcut.TargetPath = $exePath
$startMenuShortcut.WorkingDirectory = $installPath
$startMenuShortcut.Description = 'PrivacyMask for Desktop Apps'
$startMenuShortcut.Save()

Write-Host "Installed PrivacyMask to $installPath"
