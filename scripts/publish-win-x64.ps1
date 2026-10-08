param(
  [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
$productVersion = Get-PrivacyMaskProductVersion -RepoRoot $repoRoot

if (-not $SkipTests) {
  dotnet test PrivacyMask.Desktop.slnx
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
  }
}

dotnet publish src/PrivacyMask.App/PrivacyMask.App.csproj `
  -c Release `
  -p:PublishProfile=WinX64
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$buildInfoPath = Join-Path $repoRoot 'desktop-app/windows/win-x64/BUILD-INFO.txt'
$buildInfo = @(
  "PrivacyMask Windows build"
  "Version: $productVersion"
  "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
  "Runtime: win-x64 self-contained"
)

Set-Content -Path $buildInfoPath -Value $buildInfo
Write-Host "Published PrivacyMask to desktop-app/windows/win-x64/app"
