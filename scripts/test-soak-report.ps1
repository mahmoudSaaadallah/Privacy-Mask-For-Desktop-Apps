#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$reportTool = Join-Path $PSScriptRoot 'export-soak-report.ps1'
$checkpointTool = Join-Path $PSScriptRoot 'record-soak-checkpoint.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-soak-report-test-' + [guid]::NewGuid().ToString('N'))
$script:checks = 0
function Assert-Report([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
  $script:checks++
}
function Assert-Rejected([scriptblock]$Action, [string]$Message) {
  $rejected = $false
  try { & $Action | Out-Null } catch { $rejected = $true }
  Assert-Report $rejected $Message
}
try {
  $null = New-Item -ItemType Directory -Path $root
  $admission = [ordered]@{
    SchemaVersion=1; EvidenceKind='PrivacyMaskProcessSoak'; SessionId=('a' * 32); BinarySha256=('b' * 64)
    Scope='SyntheticProcessObserverTest'; ProductVersion='<img src=x onerror=alert(1)>'
    RequestedDurationSeconds=2; SampleIntervalMilliseconds=500; WarmupSeconds=0
  }
  $admission | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'admission.json') -Encoding utf8
  @'
UtcTimestamp,ElapsedSeconds,IntervalMilliseconds,WorkingSetMiB,PrivateMemoryMiB,MachineCpuPercent,Handles,Threads
2026-10-29T00:00:00Z,0.5,500,70,40,0.1,500,20
2026-10-29T00:00:01Z,1.0,500,80,41,0.2,501,21
2026-10-29T00:00:02Z,1.5,500,75,42,0.3,502,22
2026-10-29T00:00:03Z,2.0,500,71,43,0.1,500,20
'@ | Set-Content -LiteralPath (Join-Path $root 'samples.csv') -Encoding utf8
  & $reportTool -SessionDirectory $root
  $path = Join-Path $root 'report.html'
  $html = Get-Content -LiteralPath $path -Raw
  Assert-Report ($html.Contains('No final summary: running or interrupted') -and $html.Contains('Qualification: NotEvaluated')) 'Unfinished evidence was called a pass.'
  Assert-Report ($html.Contains('&lt;img') -and -not $html.Contains('<img')) 'Metadata was not HTML-encoded.'
  Assert-Report (([regex]::Matches($html,'<svg ')).Count -eq 4) 'Resource graphs missing.'
  Assert-Report (-not ($html -match '<script|<iframe|<link|<img|https?://')) 'Report loads external or executable content.'
  $hash = (Get-FileHash -LiteralPath $path).Hash
  Assert-Rejected { & $reportTool -SessionDirectory $root } 'Report evidence could be overwritten.'
  Assert-Report ((Get-FileHash -LiteralPath $path).Hash -ceq $hash) 'Rejected report overwrote evidence.'
  Assert-Rejected { & $checkpointTool -SessionDirectory $root -Check SettingsCycles -Result Pass -Cycles 24 } 'Incomplete cycles passed.'
  Assert-Rejected { & $checkpointTool -SessionDirectory $root -Check Hover -Result Pass -Cycles 25 } 'Cycles were attached to a non-cycle check.'
  Assert-Report (-not (Test-Path -LiteralPath (Join-Path $root 'checkpoints.jsonl'))) 'Rejected checkpoint created history.'
  & $checkpointTool -SessionDirectory $root -Check settingscycles -Result pass -Cycles 25
  & $checkpointTool -SessionDirectory $root -Check Hover -Result Fail
  & $checkpointTool -SessionDirectory $root -Check Hover -Result Pass
  $events = @(Get-Content -LiteralPath (Join-Path $root 'checkpoints.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
  Assert-Report ($events.Count -eq 3 -and $events[0].Check -ceq 'SettingsCycles' -and $events[0].Result -ceq 'Pass') 'Manual checkpoints were not canonicalized/appended.'
  Assert-Report ($events[1].Result -ceq 'Fail' -and $events[2].Result -ceq 'Pass') 'Earlier failure was erased.'
  $summary = [ordered]@{
    SchemaVersion=1; SessionId=$admission.SessionId; SampleCount=4; MeasurementStatus='Completed'; Reason='RequestedObservationCompleted'
    ElapsedSeconds=2; MaximumSampleGapMilliseconds=500; TargetAliveAtEnd=$true; ReachedRequestedEnd=$true; ClockContinuous=$true
    EndpointWindowsAvailable=$false
  }
  $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
  & $reportTool -SessionDirectory $root -OutputPath (Join-Path $root 'final.html')
  $html = Get-Content -LiteralPath (Join-Path $root 'final.html') -Raw
  Assert-Report ($html.Contains('Completed: RequestedObservationCompleted') -and $html.Contains('Qualification: NotEvaluated')) 'Completed short observation became qualification.'
  Assert-Report ($html.Contains('Manual operator reports') -and $html.Contains('>Fail<') -and $html.Contains('>25<')) 'Checkpoint source/failure/cycles not displayed.'
  $summary.SessionId = 'c' * 32
  $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
  Assert-Rejected { & $reportTool -SessionDirectory $root -OutputPath (Join-Path $root 'bad-session.html') } 'Different-session summary accepted.'
  $summary.SessionId = $admission.SessionId; $summary.SampleCount = 5760; $summary.ElapsedSeconds = 28800
  $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
  Assert-Rejected { & $reportTool -SessionDirectory $root -OutputPath (Join-Path $root 'bad-count.html') } 'Invented eight-hour sample count accepted.'
  $summary.SampleCount = 4; $admission.RequestedDurationSeconds = 28800
  $admission | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'admission.json') -Encoding utf8
  $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
  Assert-Rejected { & $reportTool -SessionDirectory $root -OutputPath (Join-Path $root 'bad-duration.html') } 'Short CSV accepted as eight hours.'
  Write-Host "Passed $script:checks offline soak-report/checkpoint checks; fixture data only."
}
finally {
  $resolved = [IO.Path]::GetFullPath($root)
  if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or
      -not [IO.Path]::GetFileName($resolved).StartsWith('PrivacyMask-soak-report-test-', [StringComparison]::Ordinal)) { throw 'Unsafe report fixture cleanup.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
