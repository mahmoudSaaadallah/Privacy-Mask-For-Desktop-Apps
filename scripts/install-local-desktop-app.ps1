param(
  [string]$SourcePath = '',
  [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $repoRoot 'desktop-app\windows\win-x64\Install-PrivacyMask.ps1'

& $installerPath -SourcePath $SourcePath -InstallPath $InstallPath
