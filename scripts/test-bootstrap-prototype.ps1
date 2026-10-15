#requires -Version 7.0
param([ValidateSet('SingleFile', 'NativeAot')][string]$PublishMode = 'SingleFile')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot ('artifacts/bootstrap-prototype/' + $PublishMode)
$isAot = ($PublishMode -eq 'NativeAot').ToString().ToLowerInvariant()
Push-Location $repoRoot
try {
  foreach ($item in @(
    @{ Project = 'tools/PrivacyMask.Bootstrap.Prototype'; Folder = 'helper' },
    @{ Project = 'tests/PrivacyMask.Readiness.Probe'; Folder = 'peer' }
  )) {
    dotnet publish $item.Project --configuration Release --runtime win-x64 --self-contained true --output (Join-Path $output $item.Folder) `
      "-p:PublishAot=$isAot" '-p:DebugType=None' '-p:DebugSymbols=false' '-p:TreatWarningsAsErrors=true'
    if ($LASTEXITCODE -ne 0) { throw "$PublishMode prototype publish failed. Native AOT needs the documented Windows C++ toolchain; no automatic fallback." }
  }
  dotnet run --project tests/PrivacyMask.Bootstrap.SmokeChecks --configuration Release -- `
    (Join-Path $output 'helper/PrivacyMask.Bootstrap.Prototype.exe') `
    (Join-Path $output 'peer/PrivacyMask.Readiness.Probe.exe') (Join-Path $output 'measurements.json')
  if ($LASTEXITCODE -ne 0) { throw 'Published bootstrap/readiness smoke failed.' }
  Write-Host "$PublishMode prototype checks passed. Measurement report: $output/measurements.json"
}
finally { Pop-Location }
