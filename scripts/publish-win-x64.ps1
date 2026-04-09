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
  -p:PublishProfile=WinX64

$buildInfoPath = Join-Path $repoRoot 'desktop-app/windows/win-x64/BUILD-INFO.txt'
$buildInfo = @(
  "PrivacyMask Windows build"
  "Version: 1.0.0"
  "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
  "Runtime: win-x64 self-contained"
)

Set-Content -Path $buildInfoPath -Value $buildInfo
Write-Host "Published PrivacyMask to desktop-app/windows/win-x64/app"
