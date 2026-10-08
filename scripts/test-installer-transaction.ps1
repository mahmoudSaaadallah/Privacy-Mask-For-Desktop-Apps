param(
  [string]$SourcePath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
  $SourcePath = Join-Path $repoRoot 'src\PrivacyMask.App\bin\Debug\net10.0-windows\PrivacyMask.App.exe'
}

$resolvedSourcePath = [System.IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $resolvedSourcePath -PathType Leaf)) {
  throw "Installer test source executable not found at '$resolvedSourcePath'. Build the app first."
}

$installerPath = Join-Path $repoRoot 'desktop-app\windows\win-x64\Install-PrivacyMask.ps1'
$uninstallerPath = Join-Path $repoRoot 'desktop-app\windows\win-x64\Uninstall-PrivacyMask.ps1'
$shortcutToolsPath = Join-Path $repoRoot 'desktop-app\windows\win-x64\ShortcutTools.ps1'
$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PrivacyMask-installer-test-$([Guid]::NewGuid().ToString('N'))")
$installPath = Join-Path $smokeRoot 'install'
$desktopShortcutPath = Join-Path $smokeRoot 'desktop\PrivacyMask.lnk'
$startMenuFolder = Join-Path $smokeRoot 'start-menu\PrivacyMask'
$startMenuShortcutPath = Join-Path $startMenuFolder 'PrivacyMask.lnk'
$runningTestProcess = $null

function Assert-PathExists {
  param([string]$Path, [string]$Description)

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "$Description was not created at '$Path'."
  }
}

try {
  $unsafePathRejected = $false
  try {
    & $installerPath `
      -SourcePath $resolvedSourcePath `
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
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder `
    -SkipRestart

  $installedExePath = Join-Path $installPath 'PrivacyMask.App.exe'
  Assert-PathExists -Path $installedExePath -Description 'Installed executable'
  Assert-PathExists -Path $desktopShortcutPath -Description 'Desktop shortcut'
  Assert-PathExists -Path $startMenuShortcutPath -Description 'Start Menu shortcut'

  . $shortcutToolsPath
  $expectedTarget = [System.IO.Path]::GetFullPath($installedExePath)
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
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder `
    -SkipRestart

  if ((Get-Content -LiteralPath $settingsPath -Raw) -notmatch 'preserved') {
    throw 'The installer did not preserve the existing settings file during update.'
  }

  if (Test-Path -LiteralPath (Join-Path $installPath 'obsolete.dll')) {
    throw 'The installer retained a stale binary from the previous installation.'
  }

  $rollbackMarkerPath = Join-Path $installPath 'rollback.marker'
  Set-Content -LiteralPath $rollbackMarkerPath -Value 'previous installation' -Encoding ascii
  $failureObserved = $false
  try {
    & $installerPath `
      -SourcePath $resolvedSourcePath `
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
}
