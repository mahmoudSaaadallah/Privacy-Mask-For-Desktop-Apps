param(
  [string]$ArchivePath = '',
  [string]$ExpectedTag = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
$versionIdentity = Get-PrivacyMaskVersionIdentity -RepoRoot $repoRoot
$productVersion = $versionIdentity.Version
$assetBaseName = "PrivacyMask-$productVersion-win-x64"
$assetFileName = "$assetBaseName.zip"

if (-not [string]::IsNullOrWhiteSpace($ExpectedTag) -and $ExpectedTag -cne $versionIdentity.ExpectedTag) {
  throw "Release tag '$ExpectedTag' does not match product version '$productVersion'. Expected 'v$productVersion'."
}

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
  $ArchivePath = Join-Path $repoRoot "artifacts\release\$assetBaseName.zip"
}

$resolvedArchivePath = [System.IO.Path]::GetFullPath($ArchivePath)
$checksumPath = "$resolvedArchivePath.sha256"
if (-not (Test-Path -LiteralPath $resolvedArchivePath -PathType Leaf)) {
  throw "Release archive not found at '$resolvedArchivePath'."
}

if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
  throw "Release checksum not found at '$checksumPath'."
}

$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
$checksumMatch = [Regex]::Match(
  $checksumLine,
  "^(?<hash>[0-9a-fA-F]{64})  $([Regex]::Escape($assetFileName))$")
if (-not $checksumMatch.Success) {
  throw "Release checksum has an invalid format or filename: '$checksumLine'."
}

$actualArchiveHash = (Get-FileHash -LiteralPath $resolvedArchivePath -Algorithm SHA256).Hash
if (-not [string]::Equals(
    $checksumMatch.Groups['hash'].Value,
    $actualArchiveHash,
    [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Release archive hash does not match its SHA-256 checksum file.'
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PrivacyMask-package-test-$([Guid]::NewGuid().ToString('N'))")
$extractPath = Join-Path $smokeRoot 'extracted'
$installPath = Join-Path $smokeRoot 'PrivacyMask.Desktop'
$desktopShortcutPath = Join-Path $smokeRoot 'desktop\PrivacyMask.lnk'
$startMenuFolder = Join-Path $smokeRoot 'start-menu\PrivacyMask'

try {
  Expand-Archive -LiteralPath $resolvedArchivePath -DestinationPath $extractPath

  $expectedFiles = @(
    'Install-PrivacyMask.cmd'
    'Install-PrivacyMask.ps1'
    'README.md'
    'ShortcutTools.ps1'
    'single-file\PrivacyMask.App.exe'
    'single-file\PrivacyMask.Launcher.exe'
    'Uninstall-PrivacyMask.cmd'
    'Uninstall-PrivacyMask.ps1'
  ) | Sort-Object
  $actualFiles = @(
    Get-ChildItem -LiteralPath $extractPath -Recurse -File | ForEach-Object {
      $_.FullName.Substring($extractPath.Length + 1)
    }
  ) | Sort-Object

  if ([string]::Join('|', $actualFiles) -ne [string]::Join('|', $expectedFiles)) {
    throw "Release archive contents are unexpected. Expected: $($expectedFiles -join ', '). Actual: $($actualFiles -join ', ')."
  }

  $packagedExePath = Join-Path $extractPath 'single-file\PrivacyMask.App.exe'
  $packagedLauncherPath = Join-Path $extractPath 'single-file\PrivacyMask.Launcher.exe'
  $versionInfo = (Get-Item -LiteralPath $packagedExePath).VersionInfo
  Assert-PrivacyMaskPackageVersion -Identity $versionIdentity -FileVersion $versionInfo.FileVersion -ProductVersion $versionInfo.ProductVersion

  $installerPath = Join-Path $extractPath 'Install-PrivacyMask.ps1'
  $uninstallerPath = Join-Path $extractPath 'Uninstall-PrivacyMask.ps1'
  & $installerPath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder `
    -SkipRestart

  $installedExePath = Join-Path $installPath 'app\PrivacyMask.App.exe'
  $installedLauncherPath = Join-Path $installPath 'PrivacyMask.Launcher.exe'
  if (-not (Test-Path -LiteralPath $installedExePath -PathType Leaf)) {
    throw 'Package smoke install did not create PrivacyMask.App.exe.'
  }
  if (-not (Test-Path -LiteralPath $installedLauncherPath -PathType Leaf)) {
    throw 'Package smoke install did not create PrivacyMask.Launcher.exe.'
  }

  $packagedExeHash = (Get-FileHash -LiteralPath $packagedExePath -Algorithm SHA256).Hash
  $installedExeHash = (Get-FileHash -LiteralPath $installedExePath -Algorithm SHA256).Hash
  if ($packagedExeHash -ne $installedExeHash) {
    throw 'Installed executable does not match the executable in the release archive.'
  }
  if ((Get-FileHash -LiteralPath $packagedLauncherPath -Algorithm SHA256).Hash -ne
      (Get-FileHash -LiteralPath $installedLauncherPath -Algorithm SHA256).Hash) {
    throw 'Installed launcher does not match the launcher in the release archive.'
  }

  . (Join-Path $extractPath 'ShortcutTools.ps1')
  $expectedTarget = [System.IO.Path]::GetFullPath($installedLauncherPath)
  foreach ($shortcutPath in @($desktopShortcutPath, (Join-Path $startMenuFolder 'PrivacyMask.lnk'))) {
    $actualIcon = [PrivacyMask.Installer.UnicodeShortcut]::ReadIconLocation($shortcutPath)
    if (-not [string]::Equals($actualIcon, "$expectedTarget,0", [StringComparison]::OrdinalIgnoreCase)) {
      throw 'A packaged installer shortcut does not reference the installed executable icon.'
    }
  }
  $desktopTarget = [PrivacyMask.Installer.UnicodeShortcut]::ReadTargetPath($desktopShortcutPath)
  $startMenuTarget = [PrivacyMask.Installer.UnicodeShortcut]::ReadTargetPath(
    (Join-Path $startMenuFolder 'PrivacyMask.lnk'))
  if (-not [string]::Equals($desktopTarget, $expectedTarget, [StringComparison]::OrdinalIgnoreCase) -or
      -not [string]::Equals($startMenuTarget, $expectedTarget, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'A packaged installer shortcut does not target the installed executable.'
  }

  & $uninstallerPath `
    -InstallPath $installPath `
    -DesktopShortcutPath $desktopShortcutPath `
    -StartMenuFolder $startMenuFolder

  if ((Test-Path -LiteralPath $installPath) -or
      (Test-Path -LiteralPath $desktopShortcutPath) -or
      (Test-Path -LiteralPath $startMenuFolder)) {
    throw 'Package smoke uninstall left installed files or shortcuts behind.'
  }

  Write-Host "Release package smoke test passed for PrivacyMask $productVersion."
}
finally {
  $resolvedSmokeRoot = [System.IO.Path]::GetFullPath($smokeRoot)
  $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
  if ($resolvedSmokeRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSmokeRoot)) {
    Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
  }
}
