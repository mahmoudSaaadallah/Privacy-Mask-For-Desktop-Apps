param(
  [string]$SourcePath = '',
  [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$singleFilePath = Join-Path $repoRoot 'desktop-app/windows/win-x64/single-file/PrivacyMask.App.exe'
$portableFolderPath = Join-Path $repoRoot 'desktop-app/windows/win-x64/app'

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
  if (Test-Path $singleFilePath) {
    $SourcePath = $singleFilePath
  }
  elseif (Test-Path $portableFolderPath) {
    $SourcePath = $portableFolderPath
  }
}

if (-not (Test-Path $SourcePath)) {
  throw "Published app not found. Run scripts/publish-win-x64-single-file.ps1 or scripts/publish-win-x64.ps1 first."
}

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

if ((Get-Item $SourcePath) -is [System.IO.DirectoryInfo]) {
  Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallPath -Recurse -Force
}
else {
  Copy-Item -Path $SourcePath -Destination (Join-Path $InstallPath 'PrivacyMask.App.exe') -Force
}

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
