#requires -Version 7.4
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$TrialDirectory,
  [Parameter(Mandatory=$true)][ValidateSet('C1','U1','C2')][string]$Stage,
  [switch]$RunForCurrentUser
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if (-not $RunForCurrentUser) { throw 'Explicit -RunForCurrentUser is required: this step installs the chosen trial and starts the full App.' }
if (-not [OperatingSystem]::IsWindows() -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [Runtime.InteropServices.Architecture]::X64 -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) { throw 'Requires Windows x64.' }
. (Join-Path $PSScriptRoot 'PrivacyMask.PackagingTrial.ps1')
$Stage=$Stage.ToUpperInvariant()
$trialRoot=[IO.Path]::GetFullPath($TrialDirectory)
$trial=Read-PackagingTrial $trialRoot
$variantName=Get-PackagingTrialVariant $Stage
$variant=$trial.Variants|Where-Object Name -CEQ $variantName
if (@(Get-Process -Name PrivacyMask.App -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Exit PrivacyMask from its tray menu first. This trial never stops a running App.' }
$installRoot=Join-Path $env:LOCALAPPDATA 'PrivacyMask.Desktop'
$installed=Join-Path $installRoot 'app/PrivacyMask.App.exe'
if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) { throw 'Trial needs the existing managed installation; use the ordinary installer first.' }
Assert-PackagingTrialVersion (Get-Item -LiteralPath $installed).VersionInfo.FileVersion $trial.FileVersion
$package=Join-Path $trialRoot "$variantName/package"
$repo=Split-Path -Parent $PSScriptRoot
$output=Join-Path $repo ('artifacts/full-app-trial-'+$Stage+'-'+[datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$null=New-Item -ItemType Directory -Path $output
$began=[datetime]::UtcNow
$passes=[Collections.Generic.List[object]]::new()
$admissions=[Collections.Generic.List[object]]::new()
$regions=[Collections.Generic.List[object]]::new()
$state='Interrupted'; $app=$null; $mutex=$null; $ownsMutex=$false; $packagingInstalled=$false
try {
  # Hold the current App singleton name during replacement to block competing
  # primary startup. RequireStopped below also refuses a process detected later.
  $created=$false
  $mutex=[Threading.Mutex]::new($false,'Local\PrivacyMask.Desktop.Singleton',[ref]$created)
  if (-not $created -or -not $mutex.WaitOne(0)) { throw 'Another App/startup/trial owns the singleton; nothing installed.' }
  $ownsMutex=$true
  if (@(Get-Process -Name PrivacyMask.App -ErrorAction SilentlyContinue).Count -ne 0) { throw 'App appeared before replacement; nothing installed.' }
  Assert-PackagingTrialVersion (Get-Item -LiteralPath $installed).VersionInfo.FileVersion $trial.FileVersion
  & (Join-Path $package 'Install-PrivacyMask.ps1') -SourcePath (Join-Path $package 'single-file/PrivacyMask.App.exe') `
    -LauncherSourcePath (Join-Path $package 'single-file/PrivacyMask.Launcher.exe') -SkipRestart -RequireStopped
  $packagingInstalled=$true
  $mutex.ReleaseMutex(); $ownsMutex=$false; $mutex.Dispose(); $mutex=$null
  if ((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ine $variant.AppSha256 -or
      (Get-FileHash -LiteralPath (Join-Path $installRoot 'PrivacyMask.Launcher.exe') -Algorithm SHA256).Hash -ine $trial.LauncherSha256) { throw 'Installed trial hashes differ; do not measure.' }
  if (@(Get-Process -Name PrivacyMask.App -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Another App started before measured startup; no observation.' }
  $launched=[datetime]::UtcNow
  Start-Process -FilePath (Join-Path $installRoot 'PrivacyMask.Launcher.exe') -ArgumentList '--minimized' -WindowStyle Hidden
  $wait=[Diagnostics.Stopwatch]::StartNew()
  do { Start-Sleep -Milliseconds 200; $apps=@(Get-Process -Name PrivacyMask.App -ErrorAction SilentlyContinue) }
  while ($apps.Count -eq 0 -and $wait.Elapsed.TotalSeconds -lt 20)
  if ($apps.Count -ne 1) { throw 'Trial startup did not yield exactly one App; inspect manually.' }
  $app=$apps[0]
  if ($app.Path -ine $installed -or $app.StartTime.ToUniversalTime() -lt $launched.AddSeconds(-1)) { throw 'Unexpected App instance; do not measure.' }
  if (-not ('PrivacyMask.Diagnostics.RegionQuery' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'PrivacyMask.MemoryRegions.cs') }
  Write-Host "Stage ${Stage}: $variantName. Keep settings closed and protected windows minimized/closed for all three passes."
  Write-Host "Run evidence: $output"
  for ($pass=1;$pass -le 3;$pass++) {
    $passRoot=Join-Path $output "pass-$pass"
    $warmup=if ($pass -eq 1) {30} else {0}
    & (Join-Path $PSScriptRoot 'measure-app-soak.ps1') -TargetProcessId $app.Id -ExpectedStartTimeUtc $app.StartTime.ToUniversalTime() `
      -ExpectedProductVersion $trial.FullProductVersion -ExpectedBinarySha256 $variant.AppSha256 -OutputDirectory $passRoot `
      -DurationSeconds 60 -SampleIntervalMilliseconds 500 -WarmupSeconds $warmup
    $summary=Get-Content -LiteralPath (Join-Path $passRoot 'summary.json') -Raw|ConvertFrom-Json
    $admission=Get-Content -LiteralPath (Join-Path $passRoot 'admission.json') -Raw|ConvertFrom-Json
    if ($summary.MeasurementStatus -cne 'Completed' -or $admission.Scope -cne 'FullAppResourceObservation') { throw 'Incomplete/non-App trial pass.' }
    $passes.Add($summary); $admissions.Add($admission)
    $regions.Add([PrivacyMask.Diagnostics.RegionQuery]::Capture($app.Id))
    & (Join-Path $PSScriptRoot 'export-soak-report.ps1') -SessionDirectory $passRoot
  }
  $app.Refresh()
  if ($app.HasExited -or (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ine $variant.AppSha256) { throw 'App changed at trial completion.' }
  $state='CompletedShortObservation'
}
finally {
  if ($ownsMutex) { $mutex.ReleaseMutex() }
  if ($null -ne $mutex) { $mutex.Dispose() }
  $environment=if ($admissions.Count -gt 0) {$admissions[0]} else {$null}
  [pscustomobject]@{
    SchemaVersion=1;EvidenceKind='FullAppPackagingTrialRun';Status=$state;Qualification='NotEvaluated';Stage=$Stage;Variant=$variantName;PackagingInstalled=$packagingInstalled
    TrialId=$trial.TrialId;TrialManifestSha256=(Get-FileHash -LiteralPath (Join-Path $trialRoot 'trial.json') -Algorithm SHA256).Hash
    ProductVersion=$trial.FullProductVersion;AppSha256=$variant.AppSha256;StartedAtUtc=$began.ToString('o');FinishedAtUtc=[datetime]::UtcNow.ToString('o')
    Scenario='OperatorDeclaredFreshTrayIdle';ScenarioAutomaticallyVerified=$false
    WindowsVersion=$(if($null -ne $environment){$environment.WindowsVersion}else{''})
    LogicalProcessors=$(if($null -ne $environment){$environment.LogicalProcessors}else{0})
    Passes=$passes.ToArray();Admissions=$admissions.ToArray();RegionSnapshots=$regions.ToArray()
    Limits='Short owner-declared state only. No eight-hour or functional qualification. Normal required updater remains active; no process is killed or auto-restored.'
  }|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $output 'run.json') -Encoding utf8
  if ($null -ne $app) { $app.Dispose() }
  if ($packagingInstalled -and $state -cne 'CompletedShortObservation') {
    Write-Warning "Observation interrupted after installing $variantName. No automatic restoration occurred. Inspect the App, then explicitly run C2 if needed; newer versions are never downgraded. Evidence: $output"
  }
}
$passes|ForEach-Object{[pscustomobject]@{WorkingSetMiB=$_.Metrics.WorkingSetMiB.Average;PrivateMiB=$_.Metrics.PrivateMemoryMiB.Average;CpuPercent=$_.Metrics.MachineCpuPercent.Average;Handles=$_.Metrics.Handles.Average;Coverage=$_.SamplingCoverage}}|Format-Table
Write-Host "Stage $Stage complete. App remains running. C2 explicitly restores compressed trial packaging after U1. Evidence: $output"
