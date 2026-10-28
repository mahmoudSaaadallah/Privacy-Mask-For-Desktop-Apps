#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$runner = Join-Path $PSScriptRoot 'measure-gui-baseline.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-gui-baseline-test-' + [Guid]::NewGuid().ToString('N'))
try {
  & $runner -OutputDirectory $testRoot -WarmupSeconds 0 -DurationSeconds 2 -Passes 1
  $summary = Get-Content -LiteralPath (Join-Path $testRoot 'summary.json') -Raw | ConvertFrom-Json
  if ($summary.Scenarios.Count -ne 3 -or $summary.EvidenceKind -notlike 'SyntheticHiddenGuiOnly*') { throw 'Wrong synthetic evidence scope.' }
  foreach ($scenario in $summary.Scenarios) {
    if ($scenario.Passes.Count -ne 1 -or -not $scenario.Passes[0].Completed -or
        $scenario.Identity.ProfileCount -ne 42 -or $scenario.Identity.CaptureStarted -ne $false -or
        $scenario.Identity.ProductionRuntimeStarted -ne $false) { throw 'Incomplete/unsafe benchmark scenario.' }
    if ($scenario.Passes[0].ProductVersion -cne $scenario.Identity.ProductVersion) { throw 'Measured and product identities differ.' }
  }
  $exe = Join-Path $testRoot 'bin/PrivacyMask.Gui.Benchmark.exe'
  & $exe --scenario Unsupported --duration-seconds 2
  if ($LASTEXITCODE -ne 2) { throw 'Unsupported scenario was accepted.' }
  & $exe --duration-seconds 1
  if ($LASTEXITCODE -ne 2) { throw 'Invalid lifetime was accepted.' }
  & $exe --scenario Overview --duration-seconds 2 --control-stdin false
  if ($LASTEXITCODE -ne 0) { throw 'Timer-only benchmark did not exit normally.' }
  $rejected = $false
  try { & $runner -OutputDirectory $testRoot } catch { $rejected = $true }
  if (-not $rejected) { throw 'Existing benchmark evidence could be overwritten.' }
  Write-Host 'GUI baseline runner passed: three isolated short scenarios, exact identities, full passes, argument rejection and no evidence overwrite.'
}
finally {
  $resolved = [IO.Path]::GetFullPath($testRoot)
  $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
  if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -or
      -not ([IO.Path]::GetFileName($resolved)).StartsWith('PrivacyMask-gui-baseline-test-', [StringComparison]::Ordinal)) { throw 'Unsafe GUI test cleanup target.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
