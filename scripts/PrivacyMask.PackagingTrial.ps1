# Shared developer-trial validation; never launches, installs or stops the App.
function Assert-PackagingTrialVersion([string]$InstalledFileVersion,[string]$TrialFileVersion) {
  if ($InstalledFileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $TrialFileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
      [version]$InstalledFileVersion -gt [version]$TrialFileVersion) { throw 'Trial would downgrade or cannot identify the installed file version.' }
}

function Get-PackagingTrialVariant([string]$Stage) {
  switch -CaseSensitive ($Stage) {
    'C1' { 'Compressed' }
    'U1' { 'Uncompressed' }
    'C2' { 'Compressed' }
    default { throw 'Expected stage C1, U1 or C2.' }
  }
}

function Read-PackagingTrial([string]$Directory) {
  $root=[IO.Path]::GetFullPath($Directory)
  $path=Join-Path $root 'trial.json'
  if ((Get-Item -LiteralPath $path).Length -gt 1MB) { throw 'Oversized trial manifest.' }
  $trial=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
  if ($trial.SchemaVersion -ne 1 -or $trial.EvidenceKind -cne 'FullAppPackagingTrial' -or
      $trial.TrialId -cnotmatch '^[a-f0-9]{32}$' -or $trial.SourceCommit -cnotmatch '^[a-f0-9]{40}$' -or
      -not $trial.ManualOnly -or $trial.Variants.Count -ne 2 -or
      (@($trial.Variants.Name|Sort-Object)-join ',') -cne 'Compressed,Uncompressed' -or
      $trial.FullProductVersion -cne "$($trial.ProductVersion)+$($trial.SourceCommit)") { throw 'Unsupported trial manifest.' }
  if (($trial.Variants[0].RuntimeConfiguration|ConvertTo-Json -Depth 10 -Compress) -cne
      ($trial.Variants[1].RuntimeConfiguration|ConvertTo-Json -Depth 10 -Compress)) { throw 'Trial runtimes differ.' }
  foreach ($variant in $trial.Variants) {
    if ($variant.Compression -ne ($variant.Name -ceq 'Compressed')) { throw 'Trial compression flags differ from labels.' }
    $package=[IO.Path]::GetFullPath((Join-Path $root "$($variant.Name)/package"))
    $expected=@('Install-PrivacyMask.cmd','Install-PrivacyMask.ps1','README.md','ShortcutTools.ps1',
      'single-file/PrivacyMask.App.exe','single-file/PrivacyMask.Launcher.exe','Uninstall-PrivacyMask.cmd','Uninstall-PrivacyMask.ps1')|Sort-Object
    $actual=@(Get-ChildItem -LiteralPath $package -Recurse -File|ForEach-Object {[IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/')})|Sort-Object
    if (($actual-join '|') -cne ($expected-join '|') -or
        (($variant.Files.RelativePath|Sort-Object)-join '|') -cne ($expected-join '|') -or
        @(Get-ChildItem -LiteralPath $package -Recurse -Force|Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) { throw 'Unexpected trial package contents.' }
    foreach ($file in $variant.Files) {
      if ($file.RelativePath -cnotin $expected) { throw 'Unexpected trial file path.' }
      $full=[IO.Path]::GetFullPath((Join-Path $package $file.RelativePath))
      if (-not $full.StartsWith($package+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
          ((Get-Item -LiteralPath $full).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
          (Get-Item -LiteralPath $full).Length -ne $file.Bytes -or
          (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ine $file.Sha256) { throw 'Trial payload identity changed.' }
    }
    $app=Get-Item -LiteralPath (Join-Path $package 'single-file/PrivacyMask.App.exe')
    $launcher=Get-Item -LiteralPath (Join-Path $package 'single-file/PrivacyMask.Launcher.exe')
    if ($app.VersionInfo.ProductVersion -cne $trial.FullProductVersion -or $app.VersionInfo.FileVersion -cne $trial.FileVersion -or
        $launcher.VersionInfo.ProductVersion -cne $trial.FullProductVersion -or $launcher.VersionInfo.FileVersion -cne $trial.FileVersion) { throw 'Trial PE version mismatch.' }
    $appHash=($variant.Files|Where-Object RelativePath -CEQ 'single-file/PrivacyMask.App.exe').Sha256
    $launcherHash=($variant.Files|Where-Object RelativePath -CEQ 'single-file/PrivacyMask.Launcher.exe').Sha256
    if ($appHash -ine $variant.AppSha256 -or $launcherHash -ine $trial.LauncherSha256) { throw 'Trial executable identity mismatch.' }
  }
  return $trial
}

function Get-PackagingTrialComparison($CompressedFirst,$Uncompressed,$CompressedRestored) {
  $runs=@($CompressedFirst,$Uncompressed,$CompressedRestored)
  if (($runs.Stage-join ',') -cne 'C1,U1,C2') { throw 'Comparison needs C1/U1/C2 in order.' }
  foreach ($run in $runs) {
    if ($run.EvidenceKind -cne 'FullAppPackagingTrialRun' -or $run.Status -cne 'CompletedShortObservation' -or
        $run.TrialId -cne $CompressedFirst.TrialId -or $run.TrialManifestSha256 -cne $CompressedFirst.TrialManifestSha256 -or
        $run.ProductVersion -cne $CompressedFirst.ProductVersion -or $run.Scenario -cne 'OperatorDeclaredFreshTrayIdle' -or
        $run.Variant -cne (Get-PackagingTrialVariant $run.Stage) -or $run.Passes.Count -ne 3 -or
        $run.WindowsVersion -cne $CompressedFirst.WindowsVersion -or $run.LogicalProcessors -ne $CompressedFirst.LogicalProcessors) { throw 'Incomplete or mismatched trial runs.' }
    if ($run.Passes.MeasurementStatus -ccontains 'Interrupted' -or $run.Passes.MeasurementStatus -ccontains 'Failed') { throw 'Interrupted passes cannot enter comparison.' }
    foreach ($pass in $run.Passes) {
      if ($pass.MeasurementStatus -cne 'Completed' -or $pass.ElapsedSeconds -lt 60 -or $pass.SamplingCoverage -lt 0.8) { throw 'Incomplete trial sampling.' }
    }
  }
  if ($CompressedFirst.AppSha256 -ine $CompressedRestored.AppSha256 -or $CompressedFirst.AppSha256 -ieq $Uncompressed.AppSha256) { throw 'Trial variants were not switched/restored.' }
  if ([datetimeoffset]$CompressedFirst.FinishedAtUtc -gt [datetimeoffset]$Uncompressed.StartedAtUtc -or
      [datetimeoffset]$Uncompressed.FinishedAtUtc -gt [datetimeoffset]$CompressedRestored.StartedAtUtc) { throw 'Trial stage timing is not C/U/C.' }
  $compressedPasses=@($CompressedFirst.Passes)+@($CompressedRestored.Passes)
  $means=@(foreach ($entry in @(@{Name='Compressed (C1+C2)';Passes=$compressedPasses},@{Name='Uncompressed (U1)';Passes=@($Uncompressed.Passes)})) {
    [pscustomobject]@{
      Variant=$entry.Name; Passes=$entry.Passes.Count
      WorkingSetMiB=[math]::Round(($entry.Passes.Metrics.WorkingSetMiB.Average|Measure-Object -Average).Average,3)
      PrivateMiB=[math]::Round(($entry.Passes.Metrics.PrivateMemoryMiB.Average|Measure-Object -Average).Average,3)
      MachineCpuPercent=[math]::Round(($entry.Passes.Metrics.MachineCpuPercent.Average|Measure-Object -Average).Average,4)
      Handles=[math]::Round(($entry.Passes.Metrics.Handles.Average|Measure-Object -Average).Average,2)
    }
  })
  [pscustomobject]@{EvidenceKind='FullAppPackagingTrialComparison';Qualification='NotEvaluated';TrialId=$CompressedFirst.TrialId;Means=$means
    Limits='Owner-declared fresh tray state; short C/U/C observation only, not eight-hour, leak, functional, startup or Stable qualification.'}
}

function Read-PackagingTrialRun([string]$RunDirectory,$Trial,[string]$ManifestSha256) {
  . (Join-Path $PSScriptRoot 'PrivacyMask.Soak.ps1')
  $run=Get-Content -LiteralPath (Join-Path $RunDirectory 'run.json') -Raw|ConvertFrom-Json
  if ($run.SchemaVersion -ne 1 -or $run.EvidenceKind -cne 'FullAppPackagingTrialRun' -or $run.Status -cne 'CompletedShortObservation' -or
      $run.TrialId -cne $Trial.TrialId -or $run.TrialManifestSha256 -ine $ManifestSha256 -or $run.ProductVersion -cne $Trial.FullProductVersion -or
      $run.Passes.Count -ne 3 -or $run.Admissions.Count -ne 3) { throw 'Incomplete or mismatched run packet.' }
  $variantName=Get-PackagingTrialVariant $run.Stage
  $variant=$Trial.Variants|Where-Object Name -CEQ $variantName
  if ($run.AppSha256 -ine $variant.AppSha256 -or $run.Variant -cne $variantName) { throw 'Run variant/hash mismatch.' }
  for ($pass=1;$pass -le 3;$pass++) {
    $root=Join-Path $RunDirectory "pass-$pass"
    $admission=Read-SoakAdmission $root
    $summary=Get-Content -LiteralPath (Join-Path $root 'summary.json') -Raw|ConvertFrom-Json
    $csv=@(Import-Csv -LiteralPath (Join-Path $root 'samples.csv'))
    $warmup=if ($pass -eq 1) {30} else {0}
    if ($admission.Scope -cne 'FullAppResourceObservation' -or $admission.ProductVersion -cne $Trial.FullProductVersion -or
        $admission.BinarySha256 -ine $variant.AppSha256 -or $admission.RequestedDurationSeconds -ne 60 -or
        $admission.SampleIntervalMilliseconds -ne 500 -or $admission.WarmupSeconds -ne $warmup -or
        $admission.WindowsVersion -cne $run.WindowsVersion -or $admission.LogicalProcessors -ne $run.LogicalProcessors -or
        $admission.ProcessId -ne $run.Admissions[0].ProcessId -or $admission.ProcessStartTimeUtc -ne $run.Admissions[0].ProcessStartTimeUtc -or
        $summary.SessionId -cne $admission.SessionId -or $summary.MeasurementStatus -cne 'Completed' -or $summary.SampleCount -ne $csv.Count -or
        $csv.Count -eq 0 -or $run.Passes[$pass-1].SessionId -cne $summary.SessionId) { throw 'Run raw evidence identity mismatch.' }
    $last=0d; $maximumGap=0d
    foreach ($row in $csv) {
      foreach ($name in @('ElapsedSeconds','WorkingSetMiB','PrivateMemoryMiB','MachineCpuPercent','Handles','Threads')) {
        $value=[double]::Parse($row.$name,[Globalization.CultureInfo]::InvariantCulture)
        if (-not [double]::IsFinite($value) -or $value -lt 0) { throw 'Invalid trial CSV counter.' }
      }
      $elapsed=[double]::Parse($row.ElapsedSeconds,[Globalization.CultureInfo]::InvariantCulture)
      if ($elapsed -le $last) { throw 'Trial CSV is not chronological.' }
      $gap=($elapsed-$last)*1000
      $reportedGap=[double]::Parse($row.IntervalMilliseconds,[Globalization.CultureInfo]::InvariantCulture)
      if (-not [double]::IsFinite($reportedGap) -or [math]::Abs($gap-$reportedGap) -gt 0.02) { throw 'Trial CSV interval mismatch.' }
      $maximumGap=[math]::Max($maximumGap,$gap)
      $last=$elapsed
    }
    $check=Get-SoakCompleteness -ElapsedSeconds $summary.ElapsedSeconds -LastSampleSeconds $last -RequestedSeconds 60 -Samples $csv.Count `
      -IntervalMilliseconds 500 -MaximumGapMilliseconds $maximumGap -TargetAlive $summary.TargetAliveAtEnd `
      -ReachedEnd $summary.ReachedRequestedEnd -ClockContinuous $summary.ClockContinuous
    if (-not $check.Completed) { throw 'Incomplete raw trial observation.' }
    foreach ($name in @('WorkingSetMiB','PrivateMemoryMiB','MachineCpuPercent','Handles','Threads')) {
      $average=($csv|ForEach-Object{[double]::Parse($_.$name,[Globalization.CultureInfo]::InvariantCulture)}|Measure-Object -Average).Average
      if (-not [double]::IsFinite([double]$summary.Metrics.$name.Average) -or
          [math]::Abs($average-$summary.Metrics.$name.Average) -gt 0.0002 -or
          $run.Passes[$pass-1].Metrics.$name.Average -ne $summary.Metrics.$name.Average) { throw 'Run means differ from raw numeric samples.' }
    }
  }
  return $run
}
