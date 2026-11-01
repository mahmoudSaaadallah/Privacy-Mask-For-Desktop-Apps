#requires -Version 7.0
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$runner=Join-Path $PSScriptRoot 'measure-compression-baseline.ps1'
$root=Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-compression-test-'+[guid]::NewGuid().ToString('N'))
$script:checks=0
function Assert-Compression([bool]$Condition,[string]$Message) {
  if (-not $Condition) { throw $Message }
  $script:checks++
}
function Assert-Rejected([scriptblock]$Action,[string]$Message) {
  $rejected=$false
  try { & $Action|Out-Null } catch { $rejected=$true }
  Assert-Compression $rejected $Message
}
try {
  if (-not ('PrivacyMask.Diagnostics.RegionQuery' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'PrivacyMask.MemoryRegions.cs') }
  $own=[PrivacyMask.Diagnostics.RegionQuery]::Capture($PID)
  Assert-Compression ($own.AddressEnumerationCompleted -and $own.RegionQueries -gt 10 -and $own.Categories.Count -gt 0) 'Own-process region metadata query failed.'
  Assert-Rejected { [PrivacyMask.Diagnostics.RegionQuery]::Capture(0) } 'Invalid PID accepted.'
  $forbidden=@($own.PSObject.Properties.Name|Where-Object { $_ -match 'Address|Path|Content' -and $_ -ne 'AddressEnumerationCompleted' })
  Assert-Compression ($forbidden.Count -eq 0) 'Region result exports addresses, paths or contents.'
  & $runner -OutputDirectory $root -WarmupSeconds 0 -DurationSeconds 2 -Passes 1
  $summary=Get-Content -LiteralPath (Join-Path $root 'summary.json') -Raw|ConvertFrom-Json
  Assert-Compression ($summary.EvidenceKind -ceq 'SyntheticHiddenGuiCompressionComparison' -and $summary.Qualification -ceq 'NotEvaluated') 'Wrong comparison scope.'
  Assert-Compression ($summary.Passes.Count -eq 2 -and $summary.Means.Count -eq 2 -and ($summary.Order -join ',') -ceq 'Compressed-1,Uncompressed-1') 'Incorrect pass count/order.'
  foreach($pass in $summary.Passes) {
    Assert-Compression ($pass.Measurement.Completed -and $pass.Measurement.DurationSeconds -ge 2 -and $pass.Regions.AddressEnumerationCompleted) 'Incomplete short pass accepted.'
    Assert-Compression ($pass.Identity.Distribution -ceq 'SelfContainedSingleFile' -and $pass.Identity.BenchmarkBuild.SingleFile -and
      $pass.Identity.BenchmarkBuild.SelfContained -and $pass.Identity.BenchmarkBuild.Compression -eq ($pass.Variant -ceq 'Compressed')) 'Wrong emitted deployment flags.'
    Assert-Compression (-not $pass.Identity.ProductionRuntimeStarted -and -not $pass.Identity.CaptureStarted) 'Production work started.'
    $remaining=Get-Process -Id $pass.Measurement.ProcessId -ErrorAction SilentlyContinue
    Assert-Compression ($null -eq $remaining -or $remaining.StartTime.ToUniversalTime().ToString('o') -cne $pass.Measurement.ProcessStartTimeUtc) 'Owned fixture still running.'
  }
  foreach($variant in $summary.PreparedBundles.Variants) {
    Assert-Compression ($variant.ExeBytes -gt 0 -and $variant.PayloadBytes -ge $variant.ExeBytes -and $variant.ZipBytes -gt 0) 'Missing separate footprint values.'
    Assert-Compression ($variant.RuntimeConfiguration.runtimeOptions.includedFrameworks.Count -eq 2) 'Missing self-contained runtime identity.'
  }
  $hash=(Get-FileHash -LiteralPath (Join-Path $root 'summary.json')).Hash
  Assert-Rejected { & $runner -OutputDirectory $root } 'Existing evidence overwritten.'
  Assert-Compression ((Get-FileHash -LiteralPath (Join-Path $root 'summary.json')).Hash -ceq $hash) 'Rejected run changed previous summary.'
  $bad=Join-Path $root 'mismatched-bundles'
  $null=New-Item -ItemType Directory -Path $bad
  $prepared=Get-Content -LiteralPath (Join-Path $root 'prepared.json') -Raw|ConvertFrom-Json
  $prepared.SourceCommit='0'*40
  $prepared|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $bad 'prepared.json') -Encoding utf8
  Assert-Rejected { & $runner -OutputDirectory (Join-Path $root 'rejected-source') -PreparedDirectory $bad } 'Different-source prepared fixture accepted.'
  $prepared=Get-Content -LiteralPath (Join-Path $root 'prepared.json') -Raw|ConvertFrom-Json
  $prepared.Variants[1].RuntimeConfiguration.runtimeOptions.includedFrameworks[0].version='99.0.0'
  $prepared|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $bad 'prepared.json') -Encoding utf8
  Assert-Rejected { & $runner -OutputDirectory (Join-Path $root 'rejected-runtime') -PreparedDirectory $bad } 'Mixed-runtime comparison accepted.'
  Write-Host "Passed $script:checks compression experiment checks. Synthetic seconds only; no App or Stable performance qualification."
  $global:LASTEXITCODE=0
}
finally {
  $resolved=[IO.Path]::GetFullPath($root)
  $temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
  if (-not $resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or
      -not [IO.Path]::GetFileName($resolved).StartsWith('PrivacyMask-compression-test-',[StringComparison]::Ordinal)) { throw 'Unsafe compression fixture cleanup.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
