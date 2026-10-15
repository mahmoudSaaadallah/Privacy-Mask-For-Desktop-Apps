param(
  [string]$SourcePath = '',
  [string]$LauncherSourcePath = '',
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
if ((Split-Path -Leaf ([System.IO.Path]::GetFullPath($InstallPath).TrimEnd('\', '/'))) -cne 'PrivacyMask.Desktop') {
  throw 'InstallPath must end with PrivacyMask.Desktop for the managed launcher.'
}

$buildRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$singleFilePath = Join-Path $buildRoot 'single-file\PrivacyMask.App.exe'
$singleFileLauncherPath = Join-Path $buildRoot 'single-file\PrivacyMask.Launcher.exe'
$shortcutToolsPath = Join-Path $buildRoot 'ShortcutTools.ps1'

if (-not (Test-Path -LiteralPath $shortcutToolsPath -PathType Leaf)) {
  throw "Shortcut helper not found at '$shortcutToolsPath'."
}

. $shortcutToolsPath

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
  if ((Test-Path -LiteralPath $singleFilePath -PathType Leaf) -and
      (Test-Path -LiteralPath $singleFileLauncherPath -PathType Leaf)) {
    $SourcePath = $singleFilePath
    $LauncherSourcePath = $singleFileLauncherPath
  }
  else {
    throw "Complete single-file App and launcher payload not found under '$buildRoot'."
  }
}

if ((Test-Path -LiteralPath $SourcePath -PathType Container)) {
  throw 'SourcePath must identify the self-contained single-file PrivacyMask.App.exe, not a portable directory.'
}
if ([string]::IsNullOrWhiteSpace($LauncherSourcePath)) {
  $LauncherSourcePath = Join-Path (Split-Path -Parent $SourcePath) 'PrivacyMask.Launcher.exe'
}

$resolvedSourcePath = [System.IO.Path]::GetFullPath($SourcePath)
$resolvedLauncherSourcePath = [System.IO.Path]::GetFullPath($LauncherSourcePath)
$resolvedInstallPath = [System.IO.Path]::GetFullPath($InstallPath)
$installParent = Split-Path -Parent $resolvedInstallPath
$installLeaf = Split-Path -Leaf $resolvedInstallPath
$transactionId = [Guid]::NewGuid().ToString('N')
$stagingPath = Join-Path $installParent ".$installLeaf.staging-$transactionId"
$backupPath = Join-Path $installParent ".$installLeaf.backup-$transactionId"
$legacyExePath = Join-Path $resolvedInstallPath 'PrivacyMask.App.exe'
$appDirectoryPath = Join-Path $resolvedInstallPath 'app'
$exePath = Join-Path $appDirectoryPath 'PrivacyMask.App.exe'
$launcherPath = Join-Path $resolvedInstallPath 'PrivacyMask.Launcher.exe'
$stagedAppDirectoryPath = Join-Path $stagingPath 'app'
$stagedExePath = Join-Path $stagedAppDirectoryPath 'PrivacyMask.App.exe'
$stagedLauncherPath = Join-Path $stagingPath 'PrivacyMask.Launcher.exe'
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
  param([string[]]$ExpectedExecutablePaths)

  return @(
    Get-CimInstance Win32_Process -Filter "Name = 'PrivacyMask.App.exe'" -ErrorAction SilentlyContinue |
      Where-Object {
        if ([string]::IsNullOrWhiteSpace($_.ExecutablePath)) {
          return $false
        }

        $actualPath = [System.IO.Path]::GetFullPath($_.ExecutablePath)
        return @($ExpectedExecutablePaths | Where-Object {
          [string]::Equals($actualPath, [System.IO.Path]::GetFullPath($_), [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
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

function Set-PrivateInstallTree {
  param([string]$Path)

  $user = [Security.Principal.WindowsIdentity]::GetCurrent().User
  if ($null -eq $user) { throw 'The current Windows user SID is unavailable.' }
  $items = @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force)
  foreach ($item in $items) {
    if ($item -is [System.IO.DirectoryInfo]) {
      $security = New-Object Security.AccessControl.DirectorySecurity
      $security.SetOwner($user)
      $security.SetAccessRuleProtection($true, $false)
      $rule = New-Object Security.AccessControl.FileSystemAccessRule(
        $user,
        [Security.AccessControl.FileSystemRights]::FullControl,
        [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow)
      $security.AddAccessRule($rule)
      Set-Acl -LiteralPath $item.FullName -AclObject $security
    }
    else {
      $security = New-Object Security.AccessControl.FileSecurity
      $security.SetOwner($user)
      $security.SetAccessRuleProtection($true, $false)
      $rule = New-Object Security.AccessControl.FileSystemAccessRule(
        $user,
        [Security.AccessControl.FileSystemRights]::FullControl,
        [Security.AccessControl.AccessControlType]::Allow)
      $security.AddAccessRule($rule)
      Set-Acl -LiteralPath $item.FullName -AclObject $security
    }
  }
}

function Get-OrCreateInstallId {
  param([string]$ExistingRoot)

  $markerPath = Join-Path $ExistingRoot 'managed-install.json'
  if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
    try {
      $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
      if ($marker.schemaVersion -eq 1 -and
          $marker.appId -ceq 'PrivacyMask.Desktop' -and
          $marker.updaterProtocol -eq 1 -and
          $marker.installId -cmatch '^[0-9a-f]{32}$' -and
          $marker.installId -cne ('0' * 32)) {
        return [string]$marker.installId
      }
    }
    catch { }
  }
  return [Guid]::NewGuid().ToString('N')
}

if (-not (Test-Path -LiteralPath $resolvedSourcePath -PathType Leaf)) {
  throw "Published app not found at '$resolvedSourcePath'."
}
if (-not (Test-Path -LiteralPath $resolvedLauncherSourcePath -PathType Leaf)) {
  throw "Published launcher not found at '$resolvedLauncherSourcePath'."
}

New-Item -ItemType Directory -Path $installParent -Force | Out-Null
$installId = Get-OrCreateInstallId -ExistingRoot $resolvedInstallPath
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupValueBefore = $null
if (Test-Path -LiteralPath $runKeyPath) {
  $runKey = Get-ItemProperty -LiteralPath $runKeyPath -ErrorAction SilentlyContinue
  $startupProperty = $runKey.PSObject.Properties['PrivacyMask.Desktop']
  if ($null -ne $startupProperty -and $startupProperty.Value -is [string]) {
    $startupValueBefore = [string]$startupProperty.Value
  }
}

try {
  New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
  New-Item -ItemType Directory -Path $stagedAppDirectoryPath -Force | Out-Null
  Copy-Item -LiteralPath $resolvedSourcePath -Destination $stagedExePath -Force
  Copy-Item -LiteralPath $resolvedLauncherSourcePath -Destination $stagedLauncherPath -Force

  if (-not (Test-Path -LiteralPath $stagedExePath -PathType Leaf)) {
    throw 'The prepared update does not contain PrivacyMask.App.exe.'
  }

  if ((Get-Item -LiteralPath $stagedExePath).Length -le 0) {
    throw 'The prepared PrivacyMask.App.exe is empty.'
  }
  if ((Get-Item -LiteralPath $stagedLauncherPath).Length -le 0) {
    throw 'The prepared PrivacyMask.Launcher.exe is empty.'
  }

  $runningProcesses = @(Get-InstalledPrivacyMaskProcesses -ExpectedExecutablePaths @($legacyExePath, $exePath))
  if ($runningProcesses.Count -gt 0) {
    $wasRunning = $true
    Write-Host 'Closing the running PrivacyMask instance before updating...'
    $runningProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop }
    $runningProcesses | ForEach-Object { Wait-Process -Id $_.ProcessId -Timeout 10 -ErrorAction SilentlyContinue }

    if (@(Get-InstalledPrivacyMaskProcesses -ExpectedExecutablePaths @($legacyExePath, $exePath)).Count -gt 0) {
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
  }

  $marker = [ordered]@{
    schemaVersion = 1
    appId = 'PrivacyMask.Desktop'
    installId = $installId
    updaterProtocol = 1
  } | ConvertTo-Json -Compress
  $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText((Join-Path $stagingPath 'managed-install.json'), $marker, $utf8WithoutBom)
  Set-PrivateInstallTree -Path $stagingPath

  if ($hadExistingInstall) {
    Move-Item -LiteralPath $resolvedInstallPath -Destination $backupPath
  }

  Move-Item -LiteralPath $stagingPath -Destination $resolvedInstallPath
  $replacementCompleted = $true

  $desktopShortcutDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($DesktopShortcutPath))
  New-Item -ItemType Directory -Path $desktopShortcutDirectory -Force | Out-Null
  New-PrivacyMaskShortcut `
    -ShortcutPath $DesktopShortcutPath `
    -TargetPath $launcherPath `
    -WorkingDirectory $resolvedInstallPath `
    -Description 'PrivacyMask for Desktop Apps'

  New-Item -ItemType Directory -Path $StartMenuFolder -Force | Out-Null
  New-PrivacyMaskShortcut `
    -ShortcutPath $startMenuShortcutPath `
    -TargetPath $launcherPath `
    -WorkingDirectory $resolvedInstallPath `
    -Description 'PrivacyMask for Desktop Apps'

  if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw 'PrivacyMask.App.exe was not found after installation.'
  }
  if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw 'PrivacyMask.Launcher.exe was not found after installation.'
  }

  if (-not [string]::IsNullOrWhiteSpace($startupValueBefore) -and
      ($startupValueBefore.IndexOf($legacyExePath, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
       $startupValueBefore.IndexOf($exePath, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
       $startupValueBefore.IndexOf($launcherPath, [StringComparison]::OrdinalIgnoreCase) -ge 0)) {
    $startupMinimized = $startupValueBefore.IndexOf('--minimized', [StringComparison]::OrdinalIgnoreCase) -ge 0
    $startupCommand = if ($startupMinimized) { "`"$launcherPath`" --minimized" } else { "`"$launcherPath`"" }
    New-Item -Path $runKeyPath -Force | Out-Null
    Set-ItemProperty -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop' -Value $startupCommand
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
    if ($null -eq $startupValueBefore) {
      Remove-ItemProperty -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop' -ErrorAction SilentlyContinue
    }
    else {
      New-Item -Path $runKeyPath -Force | Out-Null
      Set-ItemProperty -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop' -Value $startupValueBefore
    }
  }
  catch {
    throw "PrivacyMask installation failed and automatic rollback also failed. The backup remains at '$backupPath'. Original error: $($installError.Exception.Message). Rollback error: $($_.Exception.Message)"
  }

  if ($wasRunning -and -not $SkipRestart) {
    $restoredLauncher = Join-Path $resolvedInstallPath 'PrivacyMask.Launcher.exe'
    $restoredManagedApp = Join-Path $resolvedInstallPath 'app\PrivacyMask.App.exe'
    $restoredExecutable = if (Test-Path -LiteralPath $restoredLauncher -PathType Leaf) { $restoredLauncher }
      elseif (Test-Path -LiteralPath $restoredManagedApp -PathType Leaf) { $restoredManagedApp }
      else { $legacyExePath }
    if (Test-Path -LiteralPath $restoredExecutable -PathType Leaf) {
      Start-InstalledPrivacyMask -ExecutablePath $restoredExecutable
    }
  }

  throw $installError
}
finally {
  if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
  }
}

if ($wasRunning -and -not $SkipRestart) {
  Start-InstalledPrivacyMask -ExecutablePath $launcherPath
}

$action = if ($hadExistingInstall) { 'Updated' } else { 'Installed' }
Write-Host "$action PrivacyMask at $resolvedInstallPath"
Write-Host 'Desktop and Start Menu shortcuts are ready.'
