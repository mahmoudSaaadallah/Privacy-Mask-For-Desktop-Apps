#requires -Version 7.0
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$TargetProcessId,
  [Parameter(Mandatory = $true)][datetime]$ExpectedStartTimeUtc,
  [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ExpectedProductVersion,
  [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedBinarySha256,
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [ValidateRange(2,28800)][int]$DurationSeconds = 28800,
  [ValidateRange(100,60000)][int]$SampleIntervalMilliseconds = 5000,
  [ValidateRange(0,600)][int]$WarmupSeconds = 30,
  [ValidateRange(1,3600)][int]$EndpointWindowSeconds = 300,
  [switch]$AllowSyntheticProcess
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.Soak.ps1')
if ($SampleIntervalMilliseconds -gt $DurationSeconds * 1000) { throw 'Interval exceeds duration.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
$target = Get-Process -Id $TargetProcessId
$target.Refresh()
$started = $target.StartTime.ToUniversalTime()
if ($started -ne $ExpectedStartTimeUtc.ToUniversalTime()) { throw 'Process start identity mismatch.' }
if (-not $AllowSyntheticProcess -and $target.ProcessName -cne 'PrivacyMask.App') { throw 'Expected the full PrivacyMask.App process.' }
$binary = Get-Item -LiteralPath $target.Path
if ($binary.VersionInfo.ProductVersion -cne $ExpectedProductVersion) { throw 'Product version mismatch; no session started.' }
$sha256 = (Get-FileHash -LiteralPath $binary.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sha256 -ine $ExpectedBinarySha256) { throw 'Binary hash mismatch; no session started.' }
$target.Refresh()
if ($target.HasExited) { throw 'Target exited before admission.' }
$null = New-Item -ItemType Directory -Path $output
$admission = [pscustomobject]@{
  SchemaVersion = 1; EvidenceKind = 'PrivacyMaskProcessSoak'; SessionId = [guid]::NewGuid().ToString('N')
  AdmittedAtUtc = [datetime]::UtcNow.ToString('o'); ProcessId = $target.Id; ProcessStartTimeUtc = $started.ToString('o')
  ProductVersion = $binary.VersionInfo.ProductVersion; FileVersion = $binary.VersionInfo.FileVersion; BinarySha256 = $sha256
  Scope = $(if ($AllowSyntheticProcess) { 'SyntheticProcessObserverTest' } else { 'FullAppResourceObservation' })
  WindowsVersion = [Environment]::OSVersion.Version.ToString(); LogicalProcessors = [Environment]::ProcessorCount
  ObserverPowerShellVersion = $PSVersionTable.PSVersion.ToString(); ObserverRuntimeVersion = [Environment]::Version.ToString()
  RequestedDurationSeconds = $DurationSeconds; SampleIntervalMilliseconds = $SampleIntervalMilliseconds
  WarmupSeconds = $WarmupSeconds; EndpointWindowSeconds = $EndpointWindowSeconds
  FunctionalQualification = 'NotEvaluated'; SettingsRead = $false; WindowContentRead = $false
}
$admission | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'admission.json') -Encoding utf8
$writer = $null
$watch = [Diagnostics.Stopwatch]::new()
$status = 'Interrupted'; $reason = 'CollectorInterrupted'; $clockContinuous = $true; $reachedEnd = $false
$sampleCount = 0L; $maximumGap = 0d; $firstCount = 0L; $firstWorking = 0d; $firstHandles = 0d
$previousElapsed = 0d
$tail = [Collections.Generic.Queue[object]]::new()
$metrics = @{}
foreach ($name in @('WorkingSetMiB','PrivateMemoryMiB','MachineCpuPercent','Handles','Threads')) { $metrics[$name] = New-SoakAccumulator }
$exception = $null
try {
  $writer = [IO.StreamWriter]::new((Join-Path $output 'samples.csv'), $false, [Text.UTF8Encoding]::new($false))
  $writer.AutoFlush = $true
  $writer.WriteLine('UtcTimestamp,ElapsedSeconds,IntervalMilliseconds,WorkingSetMiB,PrivateMemoryMiB,MachineCpuPercent,Handles,Threads')
  $warmup = [Diagnostics.Stopwatch]::StartNew()
  while ($warmup.Elapsed.TotalSeconds -lt $WarmupSeconds) {
    if (Test-Path -LiteralPath (Join-Path $output 'stop.request')) { $reason = 'StopRequestedDuringWarmup'; break }
    $target.Refresh()
    if ($target.HasExited) { $reason = 'TargetExitedDuringWarmup'; break }
    Start-Sleep -Milliseconds 100
  }
  if ($reason -eq 'CollectorInterrupted') {
    $target.Refresh()
    if ($target.HasExited) { $reason = 'TargetExitedDuringWarmup' }
    else {
      $previousCpu = $target.TotalProcessorTime.TotalMilliseconds
      $previousElapsed = 0d; $previousUtc = [datetime]::UtcNow
      $nextSampleMilliseconds = [double]$SampleIntervalMilliseconds
      $watch.Start()
      while ($previousElapsed -lt $DurationSeconds) {
        if (Test-Path -LiteralPath (Join-Path $output 'stop.request')) { $reason = 'StopRequested'; break }
        Start-Sleep -Milliseconds ([int][math]::Max(1, $nextSampleMilliseconds - $watch.Elapsed.TotalMilliseconds))
        $target.Refresh()
        if ($target.HasExited) { $reason = 'TargetExitedOrRestarted'; break }
        $elapsed = $watch.Elapsed.TotalSeconds; $now = [datetime]::UtcNow
        $gap = ($elapsed - $previousElapsed) * 1000
        $maximumGap = [math]::Max($maximumGap, $gap)
        if ([math]::Abs(($now - $previousUtc).TotalMilliseconds - $gap) -gt [math]::Max(2000, 2 * $SampleIntervalMilliseconds)) {
          $clockContinuous = $false; $reason = 'ClockOrSuspendDiscontinuity'; break
        }
        if ($gap -gt [math]::Max(1000, 5 * $SampleIntervalMilliseconds)) { $reason = 'SamplingGapOrSuspend'; break }
        $cpu = $target.TotalProcessorTime.TotalMilliseconds
        $values = [ordered]@{
          WorkingSetMiB = $target.WorkingSet64 / 1MB; PrivateMemoryMiB = $target.PrivateMemorySize64 / 1MB
          MachineCpuPercent = 100d * ($cpu - $previousCpu) / ($gap * [math]::Max(1, [Environment]::ProcessorCount))
          Handles = [double]$target.HandleCount; Threads = [double]$target.Threads.Count
        }
        foreach ($name in $values.Keys) { Add-SoakValue $metrics[$name] $values[$name] }
        $numbers = @($elapsed, $gap) + @($values.Values)
        $row = @($now.ToString('o')) + @($numbers | ForEach-Object { ([double]$_).ToString('F6', [Globalization.CultureInfo]::InvariantCulture) })
        $writer.WriteLine($row -join ',')
        $sampleCount++
        if ($elapsed -le $EndpointWindowSeconds) { $firstCount++; $firstWorking += $values.WorkingSetMiB; $firstHandles += $values.Handles }
        $tail.Enqueue([pscustomobject]@{Elapsed=$elapsed; Working=$values.WorkingSetMiB; Handles=$values.Handles})
        while ($tail.Count -gt 0 -and $tail.Peek().Elapsed -lt $elapsed - $EndpointWindowSeconds) { $null = $tail.Dequeue() }
        $previousCpu = $cpu; $previousElapsed = $elapsed; $previousUtc = $now
        $nextSampleMilliseconds = [math]::Max($nextSampleMilliseconds + $SampleIntervalMilliseconds, $watch.Elapsed.TotalMilliseconds + 1)
      }
      $reachedEnd = $reason -eq 'CollectorInterrupted' -and $previousElapsed -ge $DurationSeconds
    }
  }
}
catch { $exception = $_; $reason = 'CounterOrCollectorError'; $status = 'Failed' }
finally {
  $watch.Stop()
  if ($null -ne $writer) { $writer.Dispose() }
  $target.Refresh()
  $complete = Get-SoakCompleteness -ElapsedSeconds $watch.Elapsed.TotalSeconds -LastSampleSeconds $previousElapsed -RequestedSeconds $DurationSeconds `
    -Samples $sampleCount -IntervalMilliseconds $SampleIntervalMilliseconds -MaximumGapMilliseconds $maximumGap `
    -TargetAlive (-not $target.HasExited) -ReachedEnd $reachedEnd -ClockContinuous $clockContinuous
  if ($complete.Completed) { $status = 'Completed'; $reason = 'RequestedObservationCompleted' }
  elseif ($reachedEnd) { $reason = 'InsufficientSamplingCoverage' }
  $endpointReady = $firstCount -gt 0 -and $tail.Count -gt 0 -and $watch.Elapsed.TotalSeconds -ge 2 * $EndpointWindowSeconds
  $growthWorking = $null; $growthHandles = $null
  if ($endpointReady) {
    $growthWorking = Get-SoakGrowth ($firstWorking / $firstCount) (($tail | Measure-Object Working -Average).Average)
    $growthHandles = Get-SoakGrowth ($firstHandles / $firstCount) (($tail | Measure-Object Handles -Average).Average)
  }
  $summaryMetrics = [ordered]@{}
  foreach ($name in $metrics.Keys) { $summaryMetrics[$name] = Get-SoakMetric $metrics[$name] }
  $summary = [pscustomobject]@{
    SchemaVersion = 1; SessionId = $admission.SessionId; FinishedAtUtc = [datetime]::UtcNow.ToString('o')
    MeasurementStatus = $status; Reason = $reason; QualificationStatus = 'NotEvaluated'
    ReachedRequestedEnd = $reachedEnd; TargetAliveAtEnd = -not $target.HasExited; ClockContinuous = $clockContinuous
    EightHourObservationCompleted = $complete.Completed -and $DurationSeconds -eq 28800 -and -not $AllowSyntheticProcess
    ElapsedSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 3); SampleCount = $sampleCount
    SamplingCoverage = $complete.Coverage; MaximumSampleGapMilliseconds = [math]::Round($maximumGap, 3)
    MemoryUnit = 'MiB'; CpuNormalization = 'Percent of total logical-processor capacity'; Metrics = $summaryMetrics
    EndpointWindowsAvailable = $endpointReady; WorkingSetEndpointGrowthPercent = $growthWorking; HandleEndpointGrowthPercent = $growthHandles
    HistoricalGrowthLimitPercent = 3; EndpointConditionsMatched = 'Requires manual review'
    FunctionalChecks = 'Requires manual checkpoints; counters cannot prove blur, PIN, hotkeys or responsiveness'
  }
  $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
  $target.Dispose()
}
Write-Host "Observation $status ($reason). Evidence: $output. Qualification remains NotEvaluated."
if ($null -ne $exception) { throw $exception }
if ($status -ne 'Completed') { throw 'Incomplete observation; do not mark as an eight-hour pass.' }
