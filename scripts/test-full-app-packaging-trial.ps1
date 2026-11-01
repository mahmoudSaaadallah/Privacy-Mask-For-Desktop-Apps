#requires -Version 7.4
# Offline numeric fixtures only: never installs or starts the real App.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.PackagingTrial.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-packaging-fixture-'+[guid]::NewGuid().ToString('N'))
$script:checks=0
function Assert-Trial([bool]$Condition,[string]$Message) {
  if (-not $Condition) { throw $Message }; $script:checks++
}
function Assert-Rejected([scriptblock]$Action,[string]$Message) {
  $rejected=$false; try { & $Action|Out-Null } catch { $rejected=$true }
  Assert-Trial $rejected $Message
}
function Save-Json($Value,[string]$Path) { $Value|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $Path -Encoding utf8 }
try {
  $null=New-Item -ItemType Directory -Path $root
  Assert-PackagingTrialVersion '2.0.0.17' '2.0.0.17' '2.0.0-beta.17+previous-source' '2.0.0-beta.17'
  Assert-Trial ($true) 'Same file version rejected.'
  Assert-Rejected { Assert-PackagingTrialVersion '2.0.0.18' '2.0.0.17' '2.0.0-beta.17' '2.0.0-beta.17' } 'Newer installation could be downgraded.'
  Assert-Rejected { Assert-PackagingTrialVersion 'unknown' '2.0.0.17' '2.0.0-beta.17' '2.0.0-beta.17' } 'Unidentified installation accepted.'
  Assert-Rejected { Assert-PackagingTrialVersion '2.0.0.0' '2.0.0.17' '2.0.0' '2.0.0-beta.17' } 'Stable installation could be replaced by Preview due to lower numeric revision.'
  Assert-Rejected { Assert-PackagingTrialVersion '2.0.0.17' '2.0.0.17' '2.0.0-beta.18' '2.0.0-beta.17' } 'Different product with reused file revision accepted.'
  Assert-Rejected { Get-PackagingTrialVariant 'u1' } 'Noncanonical stage accepted.'
  $optInError=''
  try { & (Join-Path $PSScriptRoot 'invoke-full-app-packaging-step.ps1') -TrialDirectory $root -Stage U1 } catch { $optInError=$_.Exception.Message }
  Assert-Trial ($optInError.StartsWith('Explicit -RunForCurrentUser is required:')) 'Personal trial did not reject at the explicit opt-in gate.'
  Assert-Rejected { & (Join-Path $PSScriptRoot 'prepare-full-app-packaging-trial.ps1') -OutputDirectory $root } 'Preparation could overwrite evidence.'
  $trial=[pscustomobject]@{TrialId=('a'*32);FullProductVersion='2.0.0-beta.17+fixture';Variants=@(
    [pscustomobject]@{Name='Compressed';AppSha256=('b'*64)},[pscustomobject]@{Name='Uncompressed';AppSha256=('c'*64)})}
  $manifestHash='d'*64
  $runs=@(foreach ($stage in @('C1','U1','C2')) {
    $stageRoot=Join-Path $root $stage; $null=New-Item -ItemType Directory -Path $stageRoot
    $name=Get-PackagingTrialVariant $stage
    $hash=($trial.Variants|Where-Object Name -CEQ $name).AppSha256
    $memory=if ($stage -ceq 'U1') {40} elseif ($stage -ceq 'C1') {100} else {110}
    $summaries=@();$admissions=@()
    for ($pass=1;$pass -le 3;$pass++) {
      $passRoot=Join-Path $stageRoot "pass-$pass"; $null=New-Item -ItemType Directory -Path $passRoot
      $admission=[pscustomobject]@{
        SchemaVersion=1;EvidenceKind='PrivacyMaskProcessSoak';SessionId=[guid]::NewGuid().ToString('N');BinarySha256=$hash
        Scope='FullAppResourceObservation';ProductVersion=$trial.FullProductVersion;ProcessId=99999;ProcessStartTimeUtc='2026-11-01T00:00:00Z'
        RequestedDurationSeconds=60;SampleIntervalMilliseconds=500;WarmupSeconds=$(if($pass -eq 1){30}else{0});WindowsVersion='fixture';LogicalProcessors=20
      }
      $metrics=[ordered]@{}
      foreach ($metric in @('WorkingSetMiB','PrivateMemoryMiB','MachineCpuPercent','Handles','Threads')) {
        $value=switch ($metric) {'WorkingSetMiB' {$memory+10} 'PrivateMemoryMiB' {$memory} 'MachineCpuPercent' {0.1} 'Handles' {600} 'Threads' {20}}
        $metrics[$metric]=[pscustomobject]@{Average=$value;Maximum=$value}
      }
      $summary=[pscustomobject]@{
        SessionId=$admission.SessionId;MeasurementStatus='Completed';ElapsedSeconds=60;SampleCount=120;SamplingCoverage=1
        MaximumSampleGapMilliseconds=500;TargetAliveAtEnd=$true;ReachedRequestedEnd=$true;ClockContinuous=$true;Metrics=[pscustomobject]$metrics
      }
      $rows=1..120|ForEach-Object{[pscustomobject]@{
        UtcTimestamp=([datetimeoffset]'2026-11-01T00:00:00Z').AddMilliseconds($_*500).ToString('o')
        ElapsedSeconds=($_/2d).ToString([Globalization.CultureInfo]::InvariantCulture);IntervalMilliseconds=500
        WorkingSetMiB=$memory+10;PrivateMemoryMiB=$memory;MachineCpuPercent='0.1';Handles=600;Threads=20
      }}
      $rows|Export-Csv -LiteralPath (Join-Path $passRoot 'samples.csv') -NoTypeInformation
      Save-Json $admission (Join-Path $passRoot 'admission.json'); Save-Json $summary (Join-Path $passRoot 'summary.json')
      $admissions+=,$admission; $summaries+=,$summary
    }
    $hour=switch ($stage) {'C1' {1} 'U1' {2} 'C2' {3}}
    $run=[pscustomobject]@{
      SchemaVersion=1;EvidenceKind='FullAppPackagingTrialRun';Status='CompletedShortObservation';Stage=$stage;Variant=$name
      TrialId=$trial.TrialId;TrialManifestSha256=$manifestHash;ProductVersion=$trial.FullProductVersion;AppSha256=$hash
      Scenario='OperatorDeclaredFreshTrayIdle';WindowsVersion='fixture';LogicalProcessors=20;Passes=$summaries;Admissions=$admissions
      StartedAtUtc="2026-11-01T0${hour}:00:00Z";FinishedAtUtc="2026-11-01T0${hour}:05:00Z"
    }
    Save-Json $run (Join-Path $stageRoot 'run.json')
    Read-PackagingTrialRun $stageRoot $trial $manifestHash
  })
  $comparison=Get-PackagingTrialComparison $runs[0] $runs[1] $runs[2]
  Assert-Trial ($comparison.Means[0].PrivateMiB -eq 105 -and $comparison.Means[1].PrivateMiB -eq 40) 'C1/C2 means or uncompressed means incorrect.'
  Assert-Trial ($comparison.Qualification -ceq 'NotEvaluated') 'Short fixture became qualification.'
  Assert-Rejected { Get-PackagingTrialComparison $runs[1] $runs[0] $runs[2] } 'Incorrect C/U/C order accepted.'
  $runs[2].TrialId='e'*32
  Assert-Rejected { Get-PackagingTrialComparison $runs[0] $runs[1] $runs[2] } 'Unrelated trial accepted.'
  $runs[2].TrialId=$trial.TrialId; $runs[2].WindowsVersion='other'
  Assert-Rejected { Get-PackagingTrialComparison $runs[0] $runs[1] $runs[2] } 'Different environment accepted.'
  $runs[2].WindowsVersion='fixture'; $runs[2].StartedAtUtc='2026-11-01T01:01:00Z'
  Assert-Rejected { Get-PackagingTrialComparison $runs[0] $runs[1] $runs[2] } 'Overlapping/reordered trial accepted.'
  $csvPath=Join-Path $root 'U1/pass-1/samples.csv'
  $original=Get-Content -LiteralPath $csvPath -Raw
  $rows=@(Import-Csv -LiteralPath $csvPath); $rows[0].PrivateMemoryMiB=900
  $rows|Export-Csv -LiteralPath $csvPath -NoTypeInformation
  Assert-Rejected { Read-PackagingTrialRun (Join-Path $root 'U1') $trial $manifestHash } 'Tampered raw samples accepted.'
  $original|Set-Content -LiteralPath $csvPath
  $rows=@(Import-Csv -LiteralPath $csvPath); $rows[-1].ElapsedSeconds='59.9'; $rows[-1].IntervalMilliseconds=400
  $rows|Export-Csv -LiteralPath $csvPath -NoTypeInformation
  Assert-Rejected { Read-PackagingTrialRun (Join-Path $root 'U1') $trial $manifestHash } 'Incomplete final sample accepted.'
  $original|Set-Content -LiteralPath $csvPath
  $rows=@(Import-Csv -LiteralPath $csvPath); $rows[0].IntervalMilliseconds=9000
  $rows|Export-Csv -LiteralPath $csvPath -NoTypeInformation
  Assert-Rejected { Read-PackagingTrialRun (Join-Path $root 'U1') $trial $manifestHash } 'Invented sample intervals accepted.'
  $original|Set-Content -LiteralPath $csvPath
  $rows=@(Import-Csv -LiteralPath $csvPath); $rows[60].UtcTimestamp='2026-11-01T04:00:00Z'
  $rows|Export-Csv -LiteralPath $csvPath -NoTypeInformation
  Assert-Rejected { Read-PackagingTrialRun (Join-Path $root 'U1') $trial $manifestHash } 'Clock discontinuity hidden by summary accepted.'
  $original|Set-Content -LiteralPath $csvPath
  Assert-Rejected { Read-PackagingTrialRun (Join-Path $root 'U1') $trial ('f'*64) } 'Different manifest identity accepted.'
  $summaryPath=Join-Path $root 'U1/pass-1/summary.json'
  $summary=Get-Content -LiteralPath $summaryPath -Raw|ConvertFrom-Json; $summary.TargetAliveAtEnd=$false
  Save-Json $summary $summaryPath
  Assert-Rejected { Read-PackagingTrialRun (Join-Path $root 'U1') $trial $manifestHash } 'Exited target accepted as complete.'
  Write-Host "Passed $script:checks offline packaging-trial checks. Fixtures only; personal installation untouched."
}
finally {
  $resolved=[IO.Path]::GetFullPath($root)
  $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+[IO.Path]::DirectorySeparatorChar
  if (-not $resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or
      -not [IO.Path]::GetFileName($resolved).StartsWith('PrivacyMask-packaging-fixture-',[StringComparison]::Ordinal)) { throw 'Unsafe fixture cleanup.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
