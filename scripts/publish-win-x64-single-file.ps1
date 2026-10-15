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
  -p:PublishProfile=WinX64SingleFile
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$singleFileOutput = Join-Path $repoRoot 'desktop-app/windows/win-x64/single-file'
dotnet publish src/PrivacyMask.Launcher/PrivacyMask.Launcher.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o $singleFileOutput `
  -p:PublishAot=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) {
  throw 'PrivacyMask launcher publish failed. Native AOT requires the Windows C++ linker workload.'
}

Get-ChildItem -Path $singleFileOutput -Filter '*.pdb' -File -ErrorAction SilentlyContinue | Remove-Item -Force

$buildInfoPath = Join-Path $repoRoot 'desktop-app/windows/win-x64/SINGLE-FILE-BUILD-INFO.txt'
$buildInfo = @(
  "PrivacyMask Windows single-file build"
  "Version: $productVersion"
  "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
  "Runtime: win-x64 self-contained App + Native AOT launcher"
)

Set-Content -Path $buildInfoPath -Value $buildInfo
Write-Host "Published PrivacyMask App and stable launcher to desktop-app/windows/win-x64/single-file"
