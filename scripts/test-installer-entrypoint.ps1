$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $temporaryBase ("PrivacyMask-entrypoint-" + [Guid]::NewGuid().ToString('N'))
$entrypoint = Join-Path $repoRoot 'desktop-app/windows/win-x64/Install-PrivacyMask.cmd'

function Write-FixtureFile {
  param([string]$Path, [string]$Text)
  New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
  $encoding = if ([System.IO.Path]::GetExtension($Path) -eq '.bat') { [System.Text.Encoding]::ASCII } else { [System.Text.Encoding]::UTF8 }
  [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Test-Dispatch {
  param(
    [string]$Name,
    [bool]$Checkout,
    [string]$PublishedFolder,
    [int]$SourceExitCode = 17
  )
  $caseRoot = Join-Path $fixtureRoot $Name
  $distributionRoot = Join-Path $caseRoot 'desktop-app/windows/win-x64'
  New-Item -ItemType Directory -Path $distributionRoot -Force | Out-Null
  Copy-Item -LiteralPath $entrypoint -Destination (Join-Path $distributionRoot 'Install-PrivacyMask.cmd')
  $marker = Join-Path $caseRoot 'dispatch.txt'
  # These stubs only record dispatch. No real install, publish, settings or startup action runs.
  Write-FixtureFile (Join-Path $caseRoot 'Install-PrivacyMask.bat') ("@echo off" + [Environment]::NewLine + 'echo source>"%PRIVACYMASK_DISPATCH_MARKER%"' + [Environment]::NewLine + "exit /b $SourceExitCode")
  Write-FixtureFile (Join-Path $distributionRoot 'Install-PrivacyMask.ps1') '[System.IO.File]::WriteAllText($env:PRIVACYMASK_DISPATCH_MARKER, "published"); exit 23'
  if ($Checkout) {
    Write-FixtureFile (Join-Path $caseRoot 'src/PrivacyMask.App/PrivacyMask.App.csproj') '<Project />'
    Write-FixtureFile (Join-Path $caseRoot 'scripts/publish-win-x64-single-file.ps1') '# Test fixture only'
  }
  if ($PublishedFolder) {
    Write-FixtureFile (Join-Path $distributionRoot "$PublishedFolder/PrivacyMask.App.exe") 'Stale fixture, never executed'
  }
  $start = New-Object System.Diagnostics.ProcessStartInfo
  $start.FileName = $env:ComSpec
  $start.Arguments = '/d /c ""' + (Join-Path $distributionRoot 'Install-PrivacyMask.cmd') + '""'
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardInput = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.EnvironmentVariables['PRIVACYMASK_DISPATCH_MARKER'] = $marker
  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = $start
  $started = $false
  try {
    $started = $process.Start()
    $process.StandardInput.WriteLine('')
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(10000)) {
      $process.Kill()
      throw "Installer entrypoint test timed out: $Name."
    }
    $expected = if ($Checkout) { 'source' } else { 'published' }
    $expectedExit = if ($Checkout) { $SourceExitCode } else { 23 }
    if (-not (Test-Path -LiteralPath $marker)) {
      throw "No dispatch marker for $Name. $($process.StandardError.ReadToEnd())"
    }
    $actual = (Get-Content -LiteralPath $marker -Raw).Trim()
    if ($actual -ne $expected -or $process.ExitCode -ne $expectedExit) {
      throw "$Name routed to '$actual' with exit $($process.ExitCode); expected '$expected' / $expectedExit."
    }
    Write-Host "Passed installer entrypoint: $Name"
  }
  finally {
    if ($started -and -not $process.HasExited) { $process.Kill() }
    $process.Dispose()
  }
}

try {
  Test-Dispatch 'checkout-stale-single-file' $true 'single-file'
  Test-Dispatch 'checkout-stale-portable' $true 'app'
  Test-Dispatch 'checkout-without-publish' $true ''
  Test-Dispatch 'checkout-build-failure-no-fallback' $true 'single-file' 41
  Test-Dispatch 'release-single-file-with-unrelated-bat' $false 'single-file'
  Test-Dispatch 'release-portable-with-unrelated-bat' $false 'app'
  Write-Host 'Installer entrypoint checks passed without installing or building an application.'
}
finally {
  $resolvedFixtureRoot = [System.IO.Path]::GetFullPath($fixtureRoot)
  if (-not $resolvedFixtureRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -or
      [System.IO.Path]::GetFileName($resolvedFixtureRoot) -notlike 'PrivacyMask-entrypoint-*') {
    throw 'Refusing to remove a fixture outside its dedicated temporary directory.'
  }
  if (Test-Path -LiteralPath $resolvedFixtureRoot) {
    Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse -Force
  }
}
