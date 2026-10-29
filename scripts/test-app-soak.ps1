#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.Soak.ps1')
$probe = Join-Path $PSScriptRoot 'measure-app-soak.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-soak-test-' + [guid]::NewGuid().ToString('N'))
$shell = (Get-Process -Id $PID).Path
$version = (Get-Item -LiteralPath $shell).VersionInfo.ProductVersion
$shellHash = (Get-FileHash -LiteralPath $shell -Algorithm SHA256).Hash
$script:checks = 0
function Assert-Soak([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
  $script:checks++
}
function Start-SoakFixture([int]$LifetimeMilliseconds = 60000) {
  $info = [Diagnostics.ProcessStartInfo]::new($shell)
  $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardOutput = $true
  foreach ($arg in @('-NoProfile','-Command',"[Console]::WriteLine('READY'); [Threading.Thread]::Sleep($LifetimeMilliseconds)")) { $info.ArgumentList.Add($arg) }
  $process = [Diagnostics.Process]::Start($info)
  $ready = $process.StandardOutput.ReadLineAsync()
  if (-not $ready.Wait(10000) -or $ready.Result -cne 'READY') { Stop-SoakFixture $process; throw 'Synthetic fixture did not start.' }
  return $process
}
function Stop-SoakFixture($Process) {
  if (-not $Process.HasExited) { $Process.Kill(); $Process.WaitForExit() }
  $Process.Dispose()
}
function Assert-Rejected([scriptblock]$Action, [string]$Message) {
  $rejected = $false
  try { & $Action | Out-Null } catch { $rejected = $true }
  Assert-Soak $rejected $Message
}
try {
  $null = New-Item -ItemType Directory -Path $root
  $acc = New-SoakAccumulator
  Add-SoakValue $acc 10; Add-SoakValue $acc 20
  Assert-Soak ((Get-SoakMetric $acc).Average -eq 15 -and (Get-SoakMetric $acc).Maximum -eq 20) 'Streaming counters are incorrect.'
  Assert-Soak ($null -eq (Get-SoakMetric (New-SoakAccumulator))) 'No samples were represented as zero usage.'
  Assert-Soak ((Get-SoakGrowth 100 103) -eq 3 -and $null -eq (Get-SoakGrowth 0 1)) 'Endpoint growth is incorrect.'
  Assert-Rejected { Add-SoakValue $acc ([double]::NaN) } 'Non-finite metric was accepted.'
  $complete = @{ElapsedSeconds=28800; LastSampleSeconds=28800; RequestedSeconds=28800; Samples=5760; IntervalMilliseconds=5000; MaximumGapMilliseconds=5000; TargetAlive=$true; ReachedEnd=$true; ClockContinuous=$true}
  Assert-Soak (Get-SoakCompleteness @complete).Completed 'Complete-duration assessment failed.'
  foreach ($change in @(@{TargetAlive=$false},@{ReachedEnd=$false},@{ClockContinuous=$false},@{Samples=100},@{MaximumGapMilliseconds=30000},@{ElapsedSeconds=28799},@{LastSampleSeconds=28799},@{LastSampleSeconds=28801})) {
    $incomplete = $complete.Clone()
    foreach ($key in $change.Keys) { $incomplete[$key] = $change[$key] }
    Assert-Soak (-not (Get-SoakCompleteness @incomplete).Completed) 'Interrupted/sparse observation was accepted.'
  }
  $process = Start-SoakFixture
  try {
    $arguments = @{TargetProcessId=$process.Id; ExpectedStartTimeUtc=$process.StartTime.ToUniversalTime(); ExpectedProductVersion=$version; ExpectedBinarySha256=$shellHash; DurationSeconds=3; SampleIntervalMilliseconds=500; WarmupSeconds=0; EndpointWindowSeconds=1; AllowSyntheticProcess=$true}
    $output = Join-Path $root 'complete'
    try { & $probe @arguments -OutputDirectory $output }
    catch { Get-Content -LiteralPath (Join-Path $output 'summary.json'); throw }
    $report = Get-Content -LiteralPath (Join-Path $output 'summary.json') -Raw | ConvertFrom-Json
    $admission = Get-Content -LiteralPath (Join-Path $output 'admission.json') -Raw | ConvertFrom-Json
    $samples = @(Import-Csv -LiteralPath (Join-Path $output 'samples.csv'))
    Assert-Soak ($report.MeasurementStatus -ceq 'Completed' -and $report.SampleCount -ge 5) 'Short observation failed.'
    Assert-Soak (-not $report.EightHourObservationCompleted -and $report.QualificationStatus -ceq 'NotEvaluated') 'Short/synthetic observation became qualification.'
    Assert-Soak ($admission.Scope -ceq 'SyntheticProcessObserverTest' -and $admission.BinarySha256.Length -eq 64) 'Binary identity or scope missing.'
    Assert-Soak ($samples.Count -eq $report.SampleCount -and [double]$samples[-1].ElapsedSeconds -ge 3) 'Streaming CSV lost samples.'
    Assert-Soak ($report.EndpointWindowsAvailable -and $null -ne $report.HandleEndpointGrowthPercent) 'Endpoint windows missing.'
    & (Join-Path $PSScriptRoot 'export-soak-report.ps1') -SessionDirectory $output
    Assert-Soak ((Get-Content -LiteralPath (Join-Path $output 'report.html') -Raw).Contains('Completed: RequestedObservationCompleted')) 'Real collector output could not be reported.'
    $hash = (Get-FileHash -LiteralPath (Join-Path $output 'summary.json')).Hash
    Assert-Rejected { & $probe @arguments -OutputDirectory $output } 'Existing evidence was overwritten.'
    Assert-Soak ((Get-FileHash -LiteralPath (Join-Path $output 'summary.json')).Hash -ceq $hash) 'Rejected invocation changed evidence.'
    foreach ($case in @('version','start','name','interval','hash')) {
      $invalid = $arguments.Clone()
      if ($case -eq 'version') { $invalid.ExpectedProductVersion = 'wrong-build' }
      if ($case -eq 'start') { $invalid.ExpectedStartTimeUtc = [datetime]::UtcNow.AddYears(-1) }
      if ($case -eq 'name') { $invalid.AllowSyntheticProcess = $false }
      if ($case -eq 'interval') { $invalid.SampleIntervalMilliseconds = 5000 }
      if ($case -eq 'hash') { $invalid.ExpectedBinarySha256 = '0' * 64 }
      $rejectedOutput = Join-Path $root $case
      Assert-Rejected { & $probe @invalid -OutputDirectory $rejectedOutput } "Unsafe admission accepted: $case"
      Assert-Soak (-not (Test-Path -LiteralPath $rejectedOutput)) "Rejected admission wrote evidence: $case"
    }
  }
  finally { Stop-SoakFixture $process }

  $process = Start-SoakFixture 1500
  try {
    $output = Join-Path $root 'warmup-exit'
    Assert-Rejected { & $probe -TargetProcessId $process.Id -ExpectedStartTimeUtc $process.StartTime.ToUniversalTime() -ExpectedProductVersion $version -ExpectedBinarySha256 $shellHash -AllowSyntheticProcess -OutputDirectory $output -WarmupSeconds 3 -DurationSeconds 2 -SampleIntervalMilliseconds 100 } 'Warm-up exit passed.'
    $report = Get-Content -LiteralPath (Join-Path $output 'summary.json') -Raw | ConvertFrom-Json
    Assert-Soak ($report.MeasurementStatus -ceq 'Interrupted' -and $report.Reason -ceq 'TargetExitedDuringWarmup' -and $report.SampleCount -eq 0) 'Warm-up exit evidence was lost.'
    & (Join-Path $PSScriptRoot 'export-soak-report.ps1') -SessionDirectory $output
    Assert-Soak ((Get-Content -LiteralPath (Join-Path $output 'report.html') -Raw).Contains('No samples.')) 'Interrupted zero-sample evidence could not be reported.'
  }
  finally { Stop-SoakFixture $process }

  $process = Start-SoakFixture 1500
  try {
    $output = Join-Path $root 'process-exit'
    Assert-Rejected { & $probe -TargetProcessId $process.Id -ExpectedStartTimeUtc $process.StartTime.ToUniversalTime() -ExpectedProductVersion $version -ExpectedBinarySha256 $shellHash -AllowSyntheticProcess -OutputDirectory $output -WarmupSeconds 0 -DurationSeconds 5 -SampleIntervalMilliseconds 100 } 'Target exit passed.'
    $report = Get-Content -LiteralPath (Join-Path $output 'summary.json') -Raw | ConvertFrom-Json
    Assert-Soak ($report.MeasurementStatus -ceq 'Interrupted' -and $report.Reason -ceq 'TargetExitedOrRestarted' -and -not $report.EightHourObservationCompleted) 'Target exit was counted as success.'
  }
  finally { Stop-SoakFixture $process }

  foreach ($warmupSeconds in @(0,5)) {
    $process = Start-SoakFixture
    $observer = $null
    try {
      $output = Join-Path $root "stop-$warmupSeconds"
      $info = [Diagnostics.ProcessStartInfo]::new($shell)
      $info.UseShellExecute = $false; $info.CreateNoWindow = $true
      $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
      foreach ($arg in @('-NoProfile','-File',$probe,'-TargetProcessId',"$($process.Id)",'-ExpectedStartTimeUtc',$process.StartTime.ToUniversalTime().ToString('o'),
          '-ExpectedProductVersion',$version,'-ExpectedBinarySha256',$shellHash,'-OutputDirectory',$output,'-DurationSeconds','15','-SampleIntervalMilliseconds','100','-WarmupSeconds',"$warmupSeconds",'-AllowSyntheticProcess')) { $info.ArgumentList.Add($arg) }
      $observer = [Diagnostics.Process]::Start($info)
      $wait = [Diagnostics.Stopwatch]::StartNew()
      $readyPath = Join-Path $output $(if ($warmupSeconds -eq 0) { 'samples.csv' } else { 'admission.json' })
      while (-not (Test-Path -LiteralPath $readyPath) -and $wait.Elapsed.TotalSeconds -lt 10 -and -not $observer.HasExited) { Start-Sleep -Milliseconds 50 }
      if ($warmupSeconds -eq 0) { Start-Sleep -Milliseconds 400 }
      $null = New-Item -ItemType File -Path (Join-Path $output 'stop.request')
      Assert-Soak ($observer.WaitForExit(10000) -and $observer.ExitCode -ne 0) 'Stopped observer passed or failed to stop.'
      $report = Get-Content -LiteralPath (Join-Path $output 'summary.json') -Raw | ConvertFrom-Json
      $expectedReason = if ($warmupSeconds -eq 0) { 'StopRequested' } else { 'StopRequestedDuringWarmup' }
      Assert-Soak ($report.MeasurementStatus -ceq 'Interrupted' -and $report.Reason -ceq $expectedReason) 'Stop evidence was incorrect.'
      Assert-Soak (-not $process.HasExited) 'Observer terminated its target.'
    }
    finally {
      if ($null -ne $observer) { Stop-SoakFixture $observer }
      Stop-SoakFixture $process
    }
  }
  $global:LASTEXITCODE = 0
  Write-Host "Passed $script:checks soak-observer checks; synthetic seconds only, no eight-hour qualification."
}
finally {
  $resolved = [IO.Path]::GetFullPath($root)
  if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or
      -not [IO.Path]::GetFileName($resolved).StartsWith('PrivacyMask-soak-test-', [StringComparison]::Ordinal)) { throw 'Unsafe soak fixture cleanup.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
