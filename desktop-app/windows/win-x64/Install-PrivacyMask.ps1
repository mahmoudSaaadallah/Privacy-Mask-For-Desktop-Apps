$ErrorActionPreference = 'Stop'

$buildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$singleFilePath = Join-Path $buildRoot 'single-file\\PrivacyMask.App.exe'
$portableFolderPath = Join-Path $buildRoot 'app'
$installPath = Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop'
$shortcutToolsPath = Join-Path $buildRoot 'ShortcutTools.ps1'

if (-not (Test-Path -LiteralPath $shortcutToolsPath -PathType Leaf)) {
  throw "Shortcut helper not found at '$shortcutToolsPath'."
}

. $shortcutToolsPath

if (Test-Path $singleFilePath) {
  $sourcePath = $singleFilePath
}
elseif (Test-Path $portableFolderPath) {
  $sourcePath = $portableFolderPath
}
else {
  throw "Published app not found under '$buildRoot'."
}

New-Item -ItemType Directory -Path $installPath -Force | Out-Null

if ((Get-Item $sourcePath) -is [System.IO.DirectoryInfo]) {
  Copy-Item -Path (Join-Path $sourcePath '*') -Destination $installPath -Recurse -Force
}
else {
  Copy-Item -Path $sourcePath -Destination (Join-Path $installPath 'PrivacyMask.App.exe') -Force
}

$exePath = Join-Path $installPath 'PrivacyMask.App.exe'

$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyMask.lnk'
New-PrivacyMaskShortcut `
  -ShortcutPath $desktopShortcutPath `
  -TargetPath $exePath `
  -WorkingDirectory $installPath `
  -Description 'PrivacyMask for Desktop Apps'

$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PrivacyMask'
New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null
$startMenuShortcutPath = Join-Path $startMenuFolder 'PrivacyMask.lnk'
New-PrivacyMaskShortcut `
  -ShortcutPath $startMenuShortcutPath `
  -TargetPath $exePath `
  -WorkingDirectory $installPath `
  -Description 'PrivacyMask for Desktop Apps'

Write-Host "Installed PrivacyMask to $installPath"
