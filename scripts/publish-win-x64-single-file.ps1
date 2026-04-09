param(
  [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $SkipTests) {
  dotnet test PrivacyMask.Desktop.slnx
}

dotnet publish src/PrivacyMask.App/PrivacyMask.App.csproj `
  -c Release `
  -p:PublishProfile=WinX64SingleFile

$singleFileOutput = Join-Path $repoRoot 'desktop-app/windows/win-x64/single-file'
Get-ChildItem -Path $singleFileOutput -Filter '*.pdb' -File -ErrorAction SilentlyContinue | Remove-Item -Force

$buildInfoPath = Join-Path $repoRoot 'desktop-app/windows/win-x64/SINGLE-FILE-BUILD-INFO.txt'
$buildInfo = @(
  "PrivacyMask Windows single-file build"
  "Version: 1.0.0"
  "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
  "Runtime: win-x64 self-contained single-file"
)

Set-Content -Path $buildInfoPath -Value $buildInfo
Write-Host "Published PrivacyMask single-file executable to desktop-app/windows/win-x64/single-file"
