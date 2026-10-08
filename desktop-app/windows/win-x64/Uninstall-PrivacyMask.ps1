param(
  [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop'),
  [string]$DesktopShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyMask.lnk'),
  [string]$StartMenuFolder = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PrivacyMask')
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

$resolvedInstallPath = [System.IO.Path]::GetFullPath($InstallPath)
$exePath = Join-Path $resolvedInstallPath 'PrivacyMask.App.exe'

$runningProcesses = @(
  Get-CimInstance Win32_Process -Filter "Name = 'PrivacyMask.App.exe'" -ErrorAction SilentlyContinue |
    Where-Object {
      -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
      [string]::Equals(
        [System.IO.Path]::GetFullPath($_.ExecutablePath),
        $exePath,
        [StringComparison]::OrdinalIgnoreCase)
    }
)

if ($runningProcesses.Count -gt 0) {
  Write-Host 'Closing PrivacyMask before uninstalling...'
  $runningProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop }
  $runningProcesses | ForEach-Object { Wait-Process -Id $_.ProcessId -Timeout 10 -ErrorAction SilentlyContinue }
}

if (Test-Path -LiteralPath $resolvedInstallPath) {
  Remove-Item -LiteralPath $resolvedInstallPath -Recurse -Force
}

if (Test-Path -LiteralPath $DesktopShortcutPath) {
  Remove-Item -LiteralPath $DesktopShortcutPath -Force
}

if (Test-Path -LiteralPath $StartMenuFolder) {
  Remove-Item -LiteralPath $StartMenuFolder -Recurse -Force
}

$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path -LiteralPath $runKeyPath) {
  $runKey = Get-ItemProperty -LiteralPath $runKeyPath -ErrorAction SilentlyContinue
  $startupProperty = $runKey.PSObject.Properties['PrivacyMask.Desktop']
  if ($null -ne $startupProperty -and
      $startupProperty.Value -is [string] -and
      $startupProperty.Value.IndexOf($exePath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    Remove-ItemProperty -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop' -ErrorAction SilentlyContinue
  }
}

Write-Host 'PrivacyMask, its shortcuts, startup entry, and local settings were removed.'
