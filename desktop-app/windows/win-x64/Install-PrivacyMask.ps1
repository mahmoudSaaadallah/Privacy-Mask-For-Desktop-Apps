param(
  [string]$SourcePath = '',
  [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop'),
  [string]$DesktopShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyMask.lnk'),
  [string]$StartMenuFolder = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PrivacyMask'),
  [switch]$SkipRestart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-SafeManagedDirectory {
  param([string]$Path, [string]$ParameterName)

  $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
  $protectedPaths = @(
    [System.IO.Path]::GetPathRoot($fullPath),
    [Environment]::GetFolderPath('UserProfile'),
    [Environment]::GetFolderPath('LocalApplicationData'),
    [Environment]::GetFolderPath('ApplicationData'),
    [System.IO.Path]::GetTempPath()
  ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
    [System.IO.Path]::GetFullPath($_).TrimEnd('\', '/')
  }

  if ([string]::IsNullOrWhiteSpace($fullPath) -or
      $protectedPaths -contains $fullPath) {
    throw "$ParameterName must point to a dedicated PrivacyMask subdirectory, not '$fullPath'."
  }
}

Assert-SafeManagedDirectory -Path $InstallPath -ParameterName 'InstallPath'
Assert-SafeManagedDirectory -Path $StartMenuFolder -ParameterName 'StartMenuFolder'

$buildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$singleFilePath = Join-Path $buildRoot 'single-file\PrivacyMask.App.exe'
$portableFolderPath = Join-Path $buildRoot 'app'
$shortcutToolsPath = Join-Path $buildRoot 'ShortcutTools.ps1'

if (-not (Test-Path -LiteralPath $shortcutToolsPath -PathType Leaf)) {
  throw "Shortcut helper not found at '$shortcutToolsPath'."
}

. $shortcutToolsPath

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
  if (Test-Path -LiteralPath $singleFilePath -PathType Leaf) {
    $SourcePath = $singleFilePath
  }
  elseif (Test-Path -LiteralPath $portableFolderPath -PathType Container) {
    $SourcePath = $portableFolderPath
  }
  else {
    throw "Published app not found under '$buildRoot'."
  }
}

$resolvedSourcePath = [System.IO.Path]::GetFullPath($SourcePath)
$resolvedInstallPath = [System.IO.Path]::GetFullPath($InstallPath)
$installParent = Split-Path -Parent $resolvedInstallPath
$installLeaf = Split-Path -Leaf $resolvedInstallPath
$transactionId = [Guid]::NewGuid().ToString('N')
$stagingPath = Join-Path $installParent ".$installLeaf.staging-$transactionId"
$backupPath = Join-Path $installParent ".$installLeaf.backup-$transactionId"
$exePath = Join-Path $resolvedInstallPath 'PrivacyMask.App.exe'
$stagedExePath = Join-Path $stagingPath 'PrivacyMask.App.exe'
$startMenuShortcutPath = Join-Path $StartMenuFolder 'PrivacyMask.lnk'
$hadExistingInstall = Test-Path -LiteralPath $resolvedInstallPath -PathType Container
$wasRunning = $false
$replacementCompleted = $false

if ([string]::Equals(
    $resolvedInstallPath,
    [System.IO.Path]::GetFullPath($StartMenuFolder).TrimEnd('\', '/'),
    [StringComparison]::OrdinalIgnoreCase)) {
  throw 'InstallPath and StartMenuFolder must be different directories.'
}

function Get-InstalledPrivacyMaskProcesses {
  param([string]$ExpectedExecutablePath)

  return @(
    Get-CimInstance Win32_Process -Filter "Name = 'PrivacyMask.App.exe'" -ErrorAction SilentlyContinue |
      Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
        [string]::Equals(
          [System.IO.Path]::GetFullPath($_.ExecutablePath),
          $ExpectedExecutablePath,
          [StringComparison]::OrdinalIgnoreCase)
      }
  )
}

function Start-InstalledPrivacyMask {
  param([string]$ExecutablePath)

  try {
    Start-Process -FilePath $ExecutablePath -ArgumentList '--minimized'
    Write-Host 'Restarted PrivacyMask after the update.'
  }
  catch {
    Write-Warning "PrivacyMask was updated but could not be restarted automatically. Start it from a shortcut. $($_.Exception.Message)"
  }
}

if (-not (Test-Path -LiteralPath $resolvedSourcePath)) {
  throw "Published app not found at '$resolvedSourcePath'."
}

New-Item -ItemType Directory -Path $installParent -Force | Out-Null

try {
  New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
  if ((Get-Item -LiteralPath $resolvedSourcePath) -is [System.IO.DirectoryInfo]) {
    Copy-Item -Path (Join-Path $resolvedSourcePath '*') -Destination $stagingPath -Recurse -Force
  }
  else {
    Copy-Item -LiteralPath $resolvedSourcePath -Destination $stagedExePath -Force
  }

  if (-not (Test-Path -LiteralPath $stagedExePath -PathType Leaf)) {
    throw 'The prepared update does not contain PrivacyMask.App.exe.'
  }

  if ((Get-Item -LiteralPath $stagedExePath).Length -le 0) {
    throw 'The prepared PrivacyMask.App.exe is empty.'
  }

  $runningProcesses = @(Get-InstalledPrivacyMaskProcesses -ExpectedExecutablePath $exePath)
  if ($runningProcesses.Count -gt 0) {
    $wasRunning = $true
    Write-Host 'Closing the running PrivacyMask instance before updating...'
    $runningProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop }
    $runningProcesses | ForEach-Object { Wait-Process -Id $_.ProcessId -Timeout 10 -ErrorAction SilentlyContinue }

    if (@(Get-InstalledPrivacyMaskProcesses -ExpectedExecutablePath $exePath).Count -gt 0) {
      throw 'PrivacyMask is still running. Close it from the tray and run the installer again.'
    }
  }

  if ($hadExistingInstall) {
    Get-ChildItem -LiteralPath $resolvedInstallPath -File -ErrorAction SilentlyContinue |
      Where-Object {
        $_.Name -eq 'settings.v1.json' -or
        $_.Name -eq 'settings.v1.json.bak' -or
        $_.Name -like 'settings.v1.corrupt-*.json'
      } |
      Copy-Item -Destination $stagingPath -Force

    Move-Item -LiteralPath $resolvedInstallPath -Destination $backupPath
  }

  Move-Item -LiteralPath $stagingPath -Destination $resolvedInstallPath
  $replacementCompleted = $true

  $desktopShortcutDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($DesktopShortcutPath))
  New-Item -ItemType Directory -Path $desktopShortcutDirectory -Force | Out-Null
  New-PrivacyMaskShortcut `
    -ShortcutPath $DesktopShortcutPath `
    -TargetPath $exePath `
    -WorkingDirectory $resolvedInstallPath `
    -Description 'PrivacyMask for Desktop Apps'

  New-Item -ItemType Directory -Path $StartMenuFolder -Force | Out-Null
  New-PrivacyMaskShortcut `
    -ShortcutPath $startMenuShortcutPath `
    -TargetPath $exePath `
    -WorkingDirectory $resolvedInstallPath `
    -Description 'PrivacyMask for Desktop Apps'

  if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw 'PrivacyMask.App.exe was not found after installation.'
  }

  if (Test-Path -LiteralPath $backupPath) {
    Remove-Item -LiteralPath $backupPath -Recurse -Force
  }
}
catch {
  $installError = $_
  try {
    if ($replacementCompleted -and (Test-Path -LiteralPath $resolvedInstallPath)) {
      Remove-Item -LiteralPath $resolvedInstallPath -Recurse -Force
    }

    if (Test-Path -LiteralPath $backupPath) {
      Move-Item -LiteralPath $backupPath -Destination $resolvedInstallPath
      Write-Warning 'The update failed, so the previous PrivacyMask installation was restored.'
    }
    elseif (-not $hadExistingInstall) {
      Remove-Item -LiteralPath $DesktopShortcutPath -Force -ErrorAction SilentlyContinue
      Remove-Item -LiteralPath $StartMenuFolder -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
  catch {
    throw "PrivacyMask installation failed and automatic rollback also failed. The backup remains at '$backupPath'. Original error: $($installError.Exception.Message). Rollback error: $($_.Exception.Message)"
  }

  if ($wasRunning -and -not $SkipRestart -and (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    Start-InstalledPrivacyMask -ExecutablePath $exePath
  }

  throw $installError
}
finally {
  if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
  }
}

if ($wasRunning -and -not $SkipRestart) {
  Start-InstalledPrivacyMask -ExecutablePath $exePath
}

$action = if ($hadExistingInstall) { 'Updated' } else { 'Installed' }
Write-Host "$action PrivacyMask at $resolvedInstallPath"
Write-Host 'Desktop and Start Menu shortcuts are ready.'
