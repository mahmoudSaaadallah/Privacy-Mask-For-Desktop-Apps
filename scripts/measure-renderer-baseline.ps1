#requires -Version 7.0
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [switch]$AllowVisibleFixtures,
  [ValidateSet('IdleFixture','OneWindow','TwoWindows','MoveOnly','MoveResize','RevealInputs')]
  [string[]]$Scenarios = @('IdleFixture','OneWindow','TwoWindows','MoveOnly','MoveResize','RevealInputs'),
  [ValidateRange(0,600)][int]$WarmupSeconds = 30,
  [ValidateRange(2,600)][int]$DurationSeconds = 60,
  [ValidateRange(1,5)][int]$Passes = 3
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $AllowVisibleFixtures) { throw 'This test opens visible synthetic windows. Explicit -AllowVisibleFixtures is required.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
if ($Scenarios.Count -eq 0 -or @($Scenarios | Select-Object -Unique).Count -ne $Scenarios.Count) { throw 'Choose unique scenarios.' }
$lifetime = $WarmupSeconds + $Passes * ($DurationSeconds + 4) + 60
if ($lifetime -gt 3600) { throw 'Session exceeds fixture lifetime limit.' }
$null = New-Item -ItemType Directory -Path $output
$commit = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Missing source identity.' }
$dirty = -not [string]::IsNullOrWhiteSpace((git -C $repoRoot status --porcelain | Out-String))
$sdk = dotnet --version
$bin = Join-Path $output 'bin'
dotnet build (Join-Path $repoRoot 'tests/PrivacyMask.Rendering.Benchmark') -c Release --artifacts-path (Join-Path $output 'build') -o $bin --nologo
if ($LASTEXITCODE -ne 0) { throw 'Renderer fixture build failed.' }
$all = [Collections.Generic.List[object]]::new()

function Read-Fixture([Diagnostics.Process]$Process) {
  $line = $Process.StandardOutput.ReadLineAsync()
  if (-not $line.Wait(30000) -or [string]::IsNullOrWhiteSpace($line.Result)) { throw 'Fixture did not respond.' }
  return ($line.Result | ConvertFrom-Json)
}

foreach ($scenario in $Scenarios) {
  $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $bin 'PrivacyMask.Rendering.Benchmark.exe'))
  $info.UseShellExecute = $false
  $info.CreateNoWindow = $true
  $info.RedirectStandardInput = $true
  $info.RedirectStandardOutput = $true
  $info.RedirectStandardError = $true
  foreach ($argument in @('--scenario',$scenario,'--duration-seconds',"$lifetime",'--allow-visible-fixtures','true')) { $info.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::Start($info)
  try {
    $identity = Read-Fixture $process
    $identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output "$scenario-admission.json") -Encoding utf8
    if ($identity.State -cne 'READY' -or $identity.EvidenceKind -cne 'LiveRendererWithOwnedSyntheticTargets' -or
        $identity.Scenario -cne $scenario -or $identity.Aborted -or $identity.FullProductionShellStarted) { throw 'Fixture is not capture-ready.' }
    $previous = @($identity.FrameCounts)
    $results = [Collections.Generic.List[object]]::new()
    $checks = [Collections.Generic.List[object]]::new()
    for ($pass = 1; $pass -le $Passes; $pass++) {
      Write-Host "Live renderer $scenario pass $pass/$Passes. Only synthetic fixture handles are captured; close a fixture to abort."
      $warmup = if ($pass -eq 1) { $WarmupSeconds } else { 0 }
      $path = Join-Path $output "$scenario-pass-$pass.json"
      & (Join-Path $PSScriptRoot 'measure-runtime.ps1') -TargetProcessId $process.Id -ExpectedStartTimeUtc $process.StartTime.ToUniversalTime() `
        -DurationSeconds $DurationSeconds -WarmupSeconds $warmup -SampleIntervalMilliseconds 500 -RequireFullDuration -OutputPath $path | Out-Null
      $process.StandardInput.WriteLine('status')
      $status = Read-Fixture $process
      if ($status.State -cne 'STATUS' -or $status.Aborted -or $status.Scenario -cne $scenario) { throw 'Fixture was interrupted.' }
      if ($scenario -ne 'IdleFixture') {
        for ($index = 0; $index -lt $status.WindowCount; $index++) {
          if (-not $status.PixelVariation[$index] -or $status.FrameCounts[$index] - $previous[$index] -lt [math]::Max(1, $DurationSeconds / 4) -or
              $status.LastFrameAgeSeconds[$index] -gt 5) { throw 'Live captures were stale, insufficient or fallback-only; reject this scenario.' }
        }
      }
      elseif (@($status.FrameCounts | Where-Object { $_ -ne 0 }).Count -ne 0) { throw 'Idle fixture unexpectedly captured content.' }
      $previous = @($status.FrameCounts)
      $checks.Add($status)
      $measurement = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
      if ($measurement.ProductVersion -cne $identity.ProductVersion) { throw 'Measured product identity mismatch.' }
      $results.Add($measurement)
    }
    $scenarioReport = [pscustomobject]@{
      Scenario = $scenario; Identity = $identity; Passes = $results.ToArray(); CaptureChecks = $checks.ToArray()
      MeanPassAverage = [pscustomobject]@{
        WorkingSetMiB = [math]::Round(($results | ForEach-Object { $_.WorkingSetMb.Average } | Measure-Object -Average).Average, 2)
        PrivateMemoryMiB = [math]::Round(($results | ForEach-Object { $_.PrivateMemoryMb.Average } | Measure-Object -Average).Average, 2)
        MachineCpuPercent = [math]::Round(($results | ForEach-Object { $_.MachineCpuPercent.Average } | Measure-Object -Average).Average, 3)
        Handles = [math]::Round(($results | ForEach-Object { $_.HandleCount.Average } | Measure-Object -Average).Average, 1)
      }
    }
    $scenarioReport | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output "$scenario-summary.json") -Encoding utf8
    $all.Add($scenarioReport)
  }
  finally {
    if (-not $process.HasExited) {
      try { $process.StandardInput.WriteLine('stop'); $process.StandardInput.Close() } catch [IO.IOException] { }
      if (-not $process.WaitForExit(20000)) { $process.Kill(); $process.WaitForExit() }
    }
    $exitCode = $process.ExitCode
    $process.Dispose()
  }
  if ($exitCode -ne 0) { throw 'Owned renderer fixture did not exit cleanly.' }
}
$report = [pscustomobject]@{
  CapturedAtUtc = [datetime]::UtcNow.ToString('o'); SourceCommit = $commit; WorkingTreeDirty = $dirty; DotnetSdk = $sdk
  ProductDllSha256 = (Get-FileHash -LiteralPath (Join-Path $bin 'PrivacyMask.App.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
  Scope = 'Production renderer plus owned synthetic WinForms targets in the same process; not full App/tray/window discovery'
  WarmupSeconds = $WarmupSeconds; PassDurationSeconds = $DurationSeconds; SampleIntervalMilliseconds = 500
  Scenarios = $all.ToArray()
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
$all | ForEach-Object { [pscustomobject]@{Scenario=$_.Scenario;WorkingSetMiB=$_.MeanPassAverage.WorkingSetMiB;PrivateMiB=$_.MeanPassAverage.PrivateMemoryMiB;CpuPercent=$_.MeanPassAverage.MachineCpuPercent;Handles=$_.MeanPassAverage.Handles} } | Format-Table
Write-Host 'Live renderer baseline completed. Real keyboard, full Shell, third-party compatibility and eight-hour stability are separate gates.'
