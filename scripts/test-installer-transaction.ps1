param(
  [string]$SourcePath = '',
  [string]$LauncherSourcePath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
  $SourcePath = Join-Path $repoRoot 'src\PrivacyMask.App\bin\Debug\net10.0-windows\PrivacyMask.App.exe'
}
if ([string]::IsNullOrWhiteSpace($LauncherSourcePath)) {
  $LauncherSourcePath = Join-Path $repoRoot 'src\PrivacyMask.Launcher\bin\Debug\net10.0-windows\win-x64\PrivacyMask.Launcher.exe'
}

$resolvedSourcePath = [System.IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $resolvedSourcePath -PathType Leaf)) {
  throw "Installer test source executable not found at '$resolvedSourcePath'. Build the app first."
}
$resolvedLauncherSourcePath = [System.IO.Path]::GetFullPath($LauncherSourcePath)
if (-not (Test-Path -LiteralPath $resolvedLauncherSourcePath -PathType Leaf)) {
  throw "Installer test launcher not found at '$resolvedLauncherSourcePath'. Build the solution first."
}

$installerPath = Join-Path $repoRoot 'desktop-app\windows\win-x64\Install-PrivacyMask.ps1'
$uninstallerPath = Join-Path $repoRoot 'desktop-app\windows\win-x64\Uninstall-PrivacyMask.ps1'
$shortcutToolsPath = Join-Path $repoRoot 'desktop-app\windows\win-x64\ShortcutTools.ps1'
$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PrivacyMask-installer-test-$([Guid]::NewGuid().ToString('N'))")
$installPath = Join-Path $smokeRoot 'PrivacyMask.Desktop'
$desktopShortcutPath = Join-Path $smokeRoot 'desktop\PrivacyMask.lnk'
$startMenuFolder = Join-Path $smokeRoot 'start-menu\PrivacyMask'
$startMenuShortcutPath = Join-Path $startMenuFolder 'PrivacyMask.lnk'
$runningTestProcess = $null
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runKey = Get-ItemProperty -LiteralPath $runKeyPath -ErrorAction SilentlyContinue
$originalStartupProperty = if ($null -ne $runKey) { $runKey.PSObject.Properties['PrivacyMask.Desktop'] } else { $null }
$hadOriginalStartupValue = $null -ne $originalStartupProperty
$originalStartupValue = if ($hadOriginalStartupValue) { $originalStartupProperty.Value } else { $null }

function Assert-PathExists {
  param([string]$Path, [string]$Description)

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "$Description was not created at '$Path'."
  }
}

function Assert-ShortcutIcons {
  param([string]$ExecutablePath)
  $expectedIcon = "$([System.IO.Path]::GetFullPath($ExecutablePath)),0"
  foreach ($shortcutPath in @($desktopShortcutPath, $startMenuShortcutPath)) {
    $actualIcon = [PrivacyMask.Installer.UnicodeShortcut]::ReadIconLocation($shortcutPath)
    if (-not [string]::Equals($actualIcon, $expectedIcon, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Shortcut '$shortcutPath' has icon '$actualIcon'; expected '$expectedIcon'."
    }
  }
}

function Assert-PrivatePath {
  param([string]$Path)
  $security = Get-Acl -LiteralPath $Path
  $user = [Security.Principal.WindowsIdentity]::GetCurrent().User
  if (-not $security.AreAccessRulesProtected -or
      -not $security.GetOwner([Security.Principal.SecurityIdentifier]).Equals($user)) {
    throw "Managed path '$Path' does not have protected current-user ownership."
  }
  $rules = @($security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
  if (@($rules | Where-Object {
      $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
      -not $_.IdentityReference.Equals($user)
    }).Count -gt 0 -or
    @($rules | Where-Object {
      $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
      $_.IdentityReference.Equals($user) -and
      ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl)
    }).Count -eq 0) {
    throw "Managed path '$Path' is accessible by an unexpected identity."
  }
}

try {
  $unsafePathRejected = $false
  try {
    & $installerPath `
      -SourcePath $resolvedSourcePath `
      -LauncherSourcePath $resolvedLauncherSourcePath `
      -InstallPath ([System.IO.Path]::GetTempPath()) `
      -DesktopShortcutPath $desktopShortcutPath `
      -StartMenuFolder $startMenuFolder `
      -SkipRestart
  }
  catch {
    $unsafePathRejected = $true
  }

  if (-not $unsafePathRejected) {
    throw 'The installer accepted an unsafe broad installation path.'
  }

  & $installerPath `
    -SourcePath $resolvedSourcePath `
    -LauncherSourcePath $resolvedLauncherSourcePath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder `
    -SkipRestart

  $installedExePath = Join-Path $installPath 'app\PrivacyMask.App.exe'
  $installedLauncherPath = Join-Path $installPath 'PrivacyMask.Launcher.exe'
  Assert-PathExists -Path $installedExePath -Description 'Installed executable'
  Assert-PathExists -Path $installedLauncherPath -Description 'Installed launcher'
  Assert-PathExists -Path (Join-Path $installPath 'managed-install.json') -Description 'Managed install marker'
  Assert-PathExists -Path $desktopShortcutPath -Description 'Desktop shortcut'
  Assert-PathExists -Path $startMenuShortcutPath -Description 'Start Menu shortcut'
  foreach ($privatePath in @($installPath, (Join-Path $installPath 'app'), $installedExePath,
      $installedLauncherPath, (Join-Path $installPath 'managed-install.json'))) {
    Assert-PrivatePath -Path $privatePath
  }
  $installId = (Get-Content -LiteralPath (Join-Path $installPath 'managed-install.json') -Raw | ConvertFrom-Json).installId

  . $shortcutToolsPath
  Assert-ShortcutIcons -ExecutablePath $installedLauncherPath
  $expectedTarget = [System.IO.Path]::GetFullPath($installedLauncherPath)
  $desktopTarget = [PrivacyMask.Installer.UnicodeShortcut]::ReadTargetPath($desktopShortcutPath)
  $startMenuTarget = [PrivacyMask.Installer.UnicodeShortcut]::ReadTargetPath($startMenuShortcutPath)
  if (-not [string]::Equals($desktopTarget, $expectedTarget, [StringComparison]::OrdinalIgnoreCase) -or
      -not [string]::Equals($startMenuTarget, $expectedTarget, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'An installer shortcut does not target the installed executable.'
  }

  Copy-Item `
    -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') `
    -Destination $installedExePath `
    -Force
  $runningTestProcess = Start-Process `
    -FilePath $installedExePath `
    -ArgumentList '-n', '60', '127.0.0.1' `
    -WindowStyle Hidden `
    -PassThru
  Start-Sleep -Milliseconds 500
  if ($runningTestProcess.HasExited) {
    throw 'The running-update test process exited before the installer could detect it.'
  }

  & $installerPath `
    -SourcePath $resolvedSourcePath `
    -LauncherSourcePath $resolvedLauncherSourcePath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder `
    -SkipRestart
  $runningTestProcess.WaitForExit(10000) | Out-Null
  if (-not $runningTestProcess.HasExited) {
    throw 'The installer did not stop the running installed executable before updating.'
  }

  $settingsPath = Join-Path $installPath 'settings.v1.json'
  Set-Content -LiteralPath $settingsPath -Value '{"preserved":true}' -Encoding UTF8
  Set-Content -LiteralPath (Join-Path $installPath 'obsolete.dll') -Value 'stale binary' -Encoding ascii

  & $installerPath `
    -SourcePath $resolvedSourcePath `
    -LauncherSourcePath $resolvedLauncherSourcePath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder `
    -SkipRestart

  if ((Get-Content -LiteralPath $settingsPath -Raw) -notmatch 'preserved') {
    throw 'The installer did not preserve the existing settings file during update.'
  }
  $updatedInstallId = (Get-Content -LiteralPath (Join-Path $installPath 'managed-install.json') -Raw | ConvertFrom-Json).installId
  if ($updatedInstallId -cne $installId) {
    throw 'The installer changed the managed installation identity during update.'
  }

  Assert-ShortcutIcons -ExecutablePath $installedLauncherPath

  if (Test-Path -LiteralPath (Join-Path $installPath 'obsolete.dll')) {
    throw 'The installer retained a stale binary from the previous installation.'
  }

  $rollbackMarkerPath = Join-Path $installPath 'rollback.marker'
  Set-Content -LiteralPath $rollbackMarkerPath -Value 'previous installation' -Encoding ascii
  $failureObserved = $false
  try {
    & $installerPath `
      -SourcePath $resolvedSourcePath `
      -LauncherSourcePath $resolvedLauncherSourcePath `
      -InstallPath $installPath `
      -DesktopShortcutPath $smokeRoot `
      -StartMenuFolder $startMenuFolder `
      -SkipRestart
  }
  catch {
    $failureObserved = $true
  }

  if (-not $failureObserved) {
    throw 'The rollback scenario did not trigger the expected shortcut failure.'
  }

  Assert-PathExists -Path $rollbackMarkerPath -Description 'Rollback marker from the previous installation'

  & $uninstallerPath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder

  if (Test-Path -LiteralPath $installPath) {
    throw 'The uninstaller did not remove the test installation.'
  }

  if ((Test-Path -LiteralPath $desktopShortcutPath) -or (Test-Path -LiteralPath $startMenuFolder)) {
    throw 'The uninstaller did not remove all test shortcuts.'
  }

  New-Item -ItemType Directory -Path $installPath -Force | Out-Null
  $legacyExePath = Join-Path $installPath 'PrivacyMask.App.exe'
  Copy-Item -LiteralPath $resolvedSourcePath -Destination $legacyExePath
  Set-Content -LiteralPath (Join-Path $installPath 'settings.v1.json') -Value '{"legacy":true}' -Encoding UTF8
  New-Item -Path $runKeyPath -Force | Out-Null
  Set-ItemProperty -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop' -Value "`"$legacyExePath`" --minimized"

  & $installerPath `
    -SourcePath $resolvedSourcePath `
    -LauncherSourcePath $resolvedLauncherSourcePath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder `
    -SkipRestart

  if ((Test-Path -LiteralPath $legacyExePath) -or
      -not (Test-Path -LiteralPath (Join-Path $installPath 'app\PrivacyMask.App.exe') -PathType Leaf)) {
    throw 'The installer did not migrate the legacy flat executable into the managed app directory.'
  }
  if ((Get-Content -LiteralPath (Join-Path $installPath 'settings.v1.json') -Raw) -notmatch 'legacy') {
    throw 'The installer did not preserve legacy settings during managed-layout migration.'
  }
  $expectedStartup = "`"$(Join-Path $installPath 'PrivacyMask.Launcher.exe')`" --minimized"
  $actualStartup = (Get-ItemPropertyValue -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop')
  if (-not [string]::Equals($actualStartup, $expectedStartup, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The installer did not migrate the legacy startup command to the stable launcher."
  }

  & $uninstallerPath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder
  $runKeyAfterUninstall = Get-ItemProperty -LiteralPath $runKeyPath -ErrorAction SilentlyContinue
  $startupAfterUninstall = if ($null -ne $runKeyAfterUninstall) {
    $runKeyAfterUninstall.PSObject.Properties['PrivacyMask.Desktop']
  } else { $null }
  if ($null -ne $startupAfterUninstall) {
    throw 'The uninstaller retained the managed launcher startup command.'
  }

  Write-Host 'Transactional installer test passed.'
}
finally {
  if ($null -ne $runningTestProcess -and -not $runningTestProcess.HasExited) {
    Stop-Process -Id $runningTestProcess.Id -Force -ErrorAction SilentlyContinue
  }

  $resolvedSmokeRoot = [System.IO.Path]::GetFullPath($smokeRoot)
  $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
  if ($resolvedSmokeRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSmokeRoot)) {
    Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
  }
  if ($hadOriginalStartupValue) {
    New-Item -Path $runKeyPath -Force | Out-Null
    Set-ItemProperty -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop' -Value $originalStartupValue
  }
  else {
    Remove-ItemProperty -LiteralPath $runKeyPath -Name 'PrivacyMask.Desktop' -ErrorAction SilentlyContinue
  }
}
