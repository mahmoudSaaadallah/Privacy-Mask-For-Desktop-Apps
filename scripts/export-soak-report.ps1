#requires -Version 7.0
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$SessionDirectory,
  [string]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.Soak.ps1')
$admission = Read-SoakAdmission $SessionDirectory
if (-not $OutputPath) { $OutputPath = Join-Path $SessionDirectory 'report.html' }
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw 'Use a new report filename; evidence is not overwritten.' }
function Encode($Value) { [Net.WebUtility]::HtmlEncode([string]$Value) }
function Number([double]$Value) { $Value.ToString('F3', [Globalization.CultureInfo]::InvariantCulture) }
$csv = Join-Path $SessionDirectory 'samples.csv'
if ((Get-Item -LiteralPath $csv).Length -gt 128MB) { throw 'Oversized soak CSV.' }
$raw = @(Import-Csv -LiteralPath $csv)
if ($raw.Count -gt 300000) { throw 'Unexpected sample count.' }
$samples = [Collections.Generic.List[object]]::new()
$fields = @('WorkingSetMiB','PrivateMemoryMiB','MachineCpuPercent','Handles','Threads')
$previousElapsed = 0d
foreach ($row in $raw) {
  $parsed = [ordered]@{}
  foreach ($field in @('ElapsedSeconds','IntervalMilliseconds') + $fields) {
    $value = [double]::Parse($row.$field, [Globalization.CultureInfo]::InvariantCulture)
    if (-not [double]::IsFinite($value) -or $value -lt 0) { throw 'Invalid CSV counter.' }
    $parsed[$field] = $value
  }
  if ($parsed.ElapsedSeconds -le $previousElapsed -or $parsed.IntervalMilliseconds -le 0) { throw 'CSV chronology is invalid.' }
  $previousElapsed = $parsed.ElapsedSeconds
  $samples.Add([pscustomobject]$parsed)
}
$state = 'No final summary: running or interrupted; incomplete evidence'
$summary = $null
$summaryPath = Join-Path $SessionDirectory 'summary.json'
if (Test-Path -LiteralPath $summaryPath) {
  $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
  if ($summary.SchemaVersion -ne 1 -or $summary.SessionId -cne $admission.SessionId -or $summary.SampleCount -ne $samples.Count -or
      $summary.MeasurementStatus -cnotin @('Completed','Interrupted','Failed')) { throw 'Summary/CSV/session mismatch.' }
  $state = "$($summary.MeasurementStatus): $($summary.Reason)"
  if ($summary.MeasurementStatus -ceq 'Completed') {
    $lastSampleSeconds = if ($samples.Count -gt 0) { $samples[-1].ElapsedSeconds } else { 0 }
    $check = Get-SoakCompleteness -ElapsedSeconds $summary.ElapsedSeconds -LastSampleSeconds $lastSampleSeconds -RequestedSeconds $admission.RequestedDurationSeconds `
      -Samples $samples.Count -IntervalMilliseconds $admission.SampleIntervalMilliseconds -MaximumGapMilliseconds $summary.MaximumSampleGapMilliseconds `
      -TargetAlive $summary.TargetAliveAtEnd -ReachedEnd $summary.ReachedRequestedEnd -ClockContinuous $summary.ClockContinuous
    if (-not $check.Completed -or $samples.Count -eq 0 -or $samples[-1].ElapsedSeconds -lt $admission.RequestedDurationSeconds) { throw 'Summary claims unsupported completion.' }
  }
}
$events = @()
$eventsPath = Join-Path $SessionDirectory 'checkpoints.jsonl'
if (Test-Path -LiteralPath $eventsPath) {
  if ((Get-Item -LiteralPath $eventsPath).Length -gt 1MB) { throw 'Oversized checkpoint history.' }
  $events = @(Get-Content -LiteralPath $eventsPath | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
  foreach ($event in $events) {
    if ($event.SessionId -cne $admission.SessionId -or $event.Source -cne 'ManualOperatorReport' -or
        $event.Check -cnotin @('TrayIdle','OneWindow','TwoWindows','MovementResize','Hover','AltExclusive','PanicPin','SettingsCycles','PauseCycles','MatchingEndpoints') -or
        $event.Result -cnotin @('Pass','Fail','NotRun')) { throw 'Invalid checkpoint history.' }
  }
}
function New-SoakChart([string]$Field, [string]$Label) {
  if ($samples.Count -eq 0) { return "<section><h2>$(Encode $Label)</h2><p>No samples.</p></section>" }
  $maximum = [math]::Max(1, ($samples | Measure-Object -Property $Field -Maximum).Maximum)
  $duration = [math]::Max(1, $samples[-1].ElapsedSeconds)
  $points = [Collections.Generic.List[string]]::new()
  # Retain each bucket's first/last/min/max in chronological order, at most 4000
  # points per chart. Full-resolution samples remain in CSV.
  $size = [int][math]::Max(1, [math]::Ceiling($samples.Count / 1000d))
  for ($start = 0; $start -lt $samples.Count; $start += $size) {
    $last = [math]::Min($samples.Count - 1, $start + $size - 1)
    $minIndex = $start; $maxIndex = $start
    for ($i = $start + 1; $i -le $last; $i++) {
      if ($samples[$i].$Field -lt $samples[$minIndex].$Field) { $minIndex = $i }
      if ($samples[$i].$Field -gt $samples[$maxIndex].$Field) { $maxIndex = $i }
    }
    foreach ($i in @($start,$minIndex,$maxIndex,$last) | Sort-Object -Unique) {
      $x = 65 + 900 * $samples[$i].ElapsedSeconds / $duration
      $y = 155 - 130 * $samples[$i].$Field / $maximum
      $points.Add("$(Number $x),$(Number $y)")
    }
  }
  return @"
<section><h2>$(Encode $Label)</h2><svg viewBox="0 0 1000 190" role="img" aria-label="$(Encode $Label) over elapsed hours">
<title>$(Encode $Label) over elapsed hours; graph buckets retain extrema</title>
<path d="M65 20 V155 H965" fill="none" stroke="#526575"/>
<text x="3" y="30">$(Number $maximum)</text><text x="35" y="157">0</text>
<text x="65" y="181">0 hours</text><text x="860" y="181">$(Number ($duration / 3600)) hours</text>
<polyline fill="none" stroke="#008d98" stroke-width="2" points="$($points -join ' ')"/></svg></section>
"@
}
$eventRows = @($events | ForEach-Object { "<tr><td>$(Encode $_.RecordedAtUtc)</td><td>$(Encode $_.Check)</td><td>$(Encode $_.Result)</td><td>$(Encode $_.Cycles)</td></tr>" }) -join ''
$missing = @('TrayIdle','OneWindow','TwoWindows','MovementResize','Hover','AltExclusive','PanicPin','SettingsCycles','PauseCycles','MatchingEndpoints') |
  Where-Object { $_ -cnotin @($events | ForEach-Object { $_.Check }) }
$growth = if ($null -ne $summary -and $summary.EndpointWindowsAvailable) {
  "Working set endpoint growth: $(Encode $summary.WorkingSetEndpointGrowthPercent)%; handles: $(Encode $summary.HandleEndpointGrowthPercent)%. Historical limit: less than 3%; matching endpoint conditions require manual review."
} else { 'Endpoint comparison unavailable; non-overlapping first/last windows are required.' }
$html = @"
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PrivacyMask soak observation</title><style>body{font:16px system-ui;color:#132c42;background:#f5f8fa;max-width:1100px;margin:30px auto;padding:0 18px}section,header{background:white;border:1px solid #dce4e9;border-radius:10px;padding:18px;margin:16px 0}h1{margin-top:0}h2{font-size:19px}svg{width:100%;font-size:13px}code{overflow-wrap:anywhere}table{width:100%;border-collapse:collapse}td,th{padding:8px;text-align:left;border-bottom:1px solid #dce4e9}.notice{background:#fff2d8;padding:14px}p{line-height:1.5}</style>
<header><h1>PrivacyMask soak observation</h1><p><strong>$(Encode $state)</strong></p>
<p class="notice">Qualification: NotEvaluated. Resource counters and operator-reported checkpoints do not automatically qualify Stable. Short and synthetic runs are tool tests, not eight-hour evidence.</p>
<p>Scope: $(Encode $admission.Scope)<br>Product: <code>$(Encode $admission.ProductVersion)</code><br>Binary SHA-256: <code>$(Encode $admission.BinarySha256)</code><br>Session: <code>$(Encode $admission.SessionId)</code></p>
<p>Requested duration: $(Encode $admission.RequestedDurationSeconds) seconds; warm-up: $(Encode $admission.WarmupSeconds) seconds; sampling: $(Encode $admission.SampleIntervalMilliseconds) ms; retained samples: $($samples.Count).</p>
<p>Memory in MiB; CPU is percent of total logical-processor capacity. $(Encode $growth)</p></header>
$(New-SoakChart 'WorkingSetMiB' 'Working set (MiB)')
$(New-SoakChart 'PrivateMemoryMiB' 'Private memory (MiB)')
$(New-SoakChart 'MachineCpuPercent' 'Whole-machine CPU (%)')
$(New-SoakChart 'Handles' 'Handles')
<section><h2>Manual operator reports (not automated assertions)</h2><p>Not recorded: $(Encode ($missing -join ', ')). All failures remain visible; a later Pass does not erase an earlier Fail.</p>
<table><thead><tr><th>Recorded UTC</th><th>Check</th><th>Result</th><th>Cycles</th></tr></thead><tbody>$eventRows</tbody></table>
<p>No PINs, profile names, window titles, screenshots or personal content are requested or exported. Sleep/resume and live recovery are separate qualification gates.</p></section></html>
"@
$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
  $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
  try { $writer.Write($html) } finally { $writer.Dispose() }
}
finally { $stream.Dispose() }
Write-Host "Saved offline report to $output. Qualification remains NotEvaluated."
