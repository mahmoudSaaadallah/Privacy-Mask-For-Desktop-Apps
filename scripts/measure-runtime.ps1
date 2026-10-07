[CmdletBinding(DefaultParameterSetName = 'ByName')]
param(
  [Parameter(Mandatory = $true, ParameterSetName = 'ById')]
  [ValidateRange(1, [int]::MaxValue)]
  [int]$TargetProcessId,

  [Parameter(ParameterSetName = 'ByName')]
  [ValidateNotNullOrEmpty()]
  [string]$ProcessName = 'PrivacyMask.App',

  [ValidateRange(2, 3600)]
  [int]$DurationSeconds = 30,

  [ValidateRange(100, 10000)]
  [int]$SampleIntervalMilliseconds = 1000,

  [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Percentile {
  param(
    [Parameter(Mandatory = $true)]
    [double[]]$Values,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100)]
    [double]$Percentile
  )

  if ($Values.Count -eq 0) {
    return 0d
  }

  $sorted = @($Values | Sort-Object)
  $index = [Math]::Ceiling(($Percentile / 100d) * $sorted.Count) - 1
  $index = [Math]::Max(0, [Math]::Min($index, $sorted.Count - 1))
  return [double]$sorted[$index]
}

function Get-MetricSummary {
  param(
    [Parameter(Mandatory = $true)]
    [double[]]$Values,

    [Parameter(Mandatory = $true)]
    [int]$Precision
  )

  if ($Values.Count -eq 0) {
    return [PSCustomObject]@{
      Average = 0
      P95 = 0
      Maximum = 0
    }
  }

  $average = ($Values | Measure-Object -Average).Average
  $maximum = ($Values | Measure-Object -Maximum).Maximum

  return [PSCustomObject]@{
    Average = [Math]::Round($average, $Precision)
    P95 = [Math]::Round((Get-Percentile -Values $Values -Percentile 95), $Precision)
    Maximum = [Math]::Round($maximum, $Precision)
  }
}

function Resolve-TargetProcess {
  if ($PSCmdlet.ParameterSetName -eq 'ById') {
    return Get-Process -Id $TargetProcessId
  }

  $matches = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
  if ($matches.Count -eq 0) {
    throw "No process named '$ProcessName' is currently running."
  }

  if ($matches.Count -gt 1) {
    $identifiers = ($matches | Select-Object -ExpandProperty Id) -join ', '
    throw "More than one process named '$ProcessName' is running ($identifiers). Use -TargetProcessId."
  }

  return $matches[0]
}

$target = Resolve-TargetProcess
$target.Refresh()

$fileVersion = ''
$productVersion = ''
try {
  if (-not [string]::IsNullOrWhiteSpace($target.Path)) {
    $versionInfo = (Get-Item -LiteralPath $target.Path).VersionInfo
    $fileVersion = $versionInfo.FileVersion
    $productVersion = $versionInfo.ProductVersion
  }
}
catch {
  # Process metrics are still useful when access to executable metadata is denied.
}

$logicalProcessorCount = [Math]::Max(1, [Environment]::ProcessorCount)
$samples = New-Object System.Collections.Generic.List[object]
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$previousSampleTimeMilliseconds = 0d
$previousCpuMilliseconds = $target.TotalProcessorTime.TotalMilliseconds

while ($stopwatch.Elapsed.TotalSeconds -lt $DurationSeconds) {
  Start-Sleep -Milliseconds $SampleIntervalMilliseconds

  try {
    $target.Refresh()
    if ($target.HasExited) {
      break
    }
  }
  catch {
    break
  }

  $elapsedMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
  $cpuMilliseconds = $target.TotalProcessorTime.TotalMilliseconds
  $intervalMilliseconds = $elapsedMilliseconds - $previousSampleTimeMilliseconds
  $cpuDeltaMilliseconds = $cpuMilliseconds - $previousCpuMilliseconds
  $machineCpuPercent = if ($intervalMilliseconds -gt 0) {
    100d * $cpuDeltaMilliseconds / ($intervalMilliseconds * $logicalProcessorCount)
  }
  else {
    0d
  }

  $samples.Add([PSCustomObject]@{
    ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
    WorkingSetMb = $target.WorkingSet64 / 1MB
    PrivateMemoryMb = $target.PrivateMemorySize64 / 1MB
    MachineCpuPercent = $machineCpuPercent
    HandleCount = [double]$target.HandleCount
    ThreadCount = [double]$target.Threads.Count
  })

  $previousSampleTimeMilliseconds = $elapsedMilliseconds
  $previousCpuMilliseconds = $cpuMilliseconds
}

$stopwatch.Stop()
if ($samples.Count -eq 0) {
  throw 'The target process exited or could not be sampled.'
}

$workingSetValues = [double[]]@($samples | ForEach-Object { $_.WorkingSetMb })
$privateMemoryValues = [double[]]@($samples | ForEach-Object { $_.PrivateMemoryMb })
$cpuValues = [double[]]@($samples | ForEach-Object { $_.MachineCpuPercent })
$handleValues = [double[]]@($samples | ForEach-Object { $_.HandleCount })
$threadValues = [double[]]@($samples | ForEach-Object { $_.ThreadCount })

$result = [PSCustomObject]@{
  CapturedAtUtc = [DateTime]::UtcNow.ToString('o')
  ProcessName = $target.ProcessName
  ProcessId = $target.Id
  FileVersion = $fileVersion
  ProductVersion = $productVersion
  DurationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
  SampleIntervalMilliseconds = $SampleIntervalMilliseconds
  SampleCount = $samples.Count
  LogicalProcessorCount = $logicalProcessorCount
  WorkingSetMb = Get-MetricSummary -Values $workingSetValues -Precision 2
  PrivateMemoryMb = Get-MetricSummary -Values $privateMemoryValues -Precision 2
  MachineCpuPercent = Get-MetricSummary -Values $cpuValues -Precision 3
  HandleCount = Get-MetricSummary -Values $handleValues -Precision 0
  ThreadCount = Get-MetricSummary -Values $threadValues -Precision 0
}

$result | Format-List

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
  $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
  $outputDirectory = Split-Path -Parent $resolvedOutputPath
  if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
  }

  $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
  Write-Host "Saved runtime measurements to $resolvedOutputPath"
}
