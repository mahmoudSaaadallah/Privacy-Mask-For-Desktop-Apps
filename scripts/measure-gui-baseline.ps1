#requires -Version 7.0
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [ValidateSet('Overview','Applications','Settings')][string[]]$Scenarios = @('Overview','Applications','Settings'),
  [ValidateRange(0,600)][int]$WarmupSeconds = 30,
  [ValidateRange(2,600)][int]$DurationSeconds = 60,
  [ValidateRange(1,5)][int]$Passes = 3,
  [ValidateRange(100,10000)][int]$SampleIntervalMilliseconds = 500
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; existing measurement evidence is never overwritten.' }
if (@($Scenarios | Select-Object -Unique).Count -ne $Scenarios.Count -or $Scenarios.Count -eq 0) { throw 'Scenarios must be nonempty and unique.' }
$lifetime = $WarmupSeconds + $Passes * ($DurationSeconds + 2) + 30
if ($lifetime -gt 3600) { throw 'Requested session exceeds the benchmark lifetime limit.' }
$null = New-Item -ItemType Directory -Path $output
$build = Join-Path $output 'build'
$binaries = Join-Path $output 'bin'
$project = Join-Path $repoRoot 'tests/PrivacyMask.Gui.Benchmark/PrivacyMask.Gui.Benchmark.csproj'
$probe = Join-Path $PSScriptRoot 'measure-runtime.ps1'
$commit = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'A source identity is required.' }
$workingTreeDirty = -not [string]::IsNullOrWhiteSpace((git -C $repoRoot status --porcelain --untracked-files=normal | Out-String))
$sdk = dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'SDK identity unavailable.' }
$expectedVersion = ([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
dotnet build $project -c Release --artifacts-path $build -o $binaries --nologo
if ($LASTEXITCODE -ne 0) { throw 'Synthetic GUI benchmark build failed.' }
$executable = Join-Path $binaries 'PrivacyMask.Gui.Benchmark.exe'
$product = Join-Path $binaries 'PrivacyMask.App.dll'
$productHash = (Get-FileHash -LiteralPath $product -Algorithm SHA256).Hash.ToLowerInvariant()
$reports = [Collections.Generic.List[object]]::new()

foreach ($scenario in $Scenarios) {
  $start = [Diagnostics.ProcessStartInfo]::new($executable)
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardInput = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  foreach ($argument in @('--scenario',$scenario,'--duration-seconds',"$lifetime",'--control-stdin','true')) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::Start($start)
  try {
    $ready = $process.StandardOutput.ReadLineAsync()
    if (-not $ready.Wait(30000) -or $process.HasExited -or [string]::IsNullOrWhiteSpace($ready.Result)) {
      throw 'Synthetic benchmark did not confirm readiness within 30 seconds.'
    }
    $identity = $ready.Result | ConvertFrom-Json
    if ($identity.State -cne 'READY' -or $identity.EvidenceKind -cne 'SyntheticHiddenGui' -or
        $identity.Scenario -cne $scenario -or $identity.ProfileCount -ne 42 -or
        $identity.ProductionRuntimeStarted -ne $false -or $identity.CaptureStarted -ne $false -or
        $identity.ProductVersion.Split('+')[0] -cne $expectedVersion) { throw 'Unexpected benchmark identity.' }
    $startTime = $process.StartTime.ToUniversalTime()
    $passesForScenario = [Collections.Generic.List[object]]::new()
    for ($pass = 1; $pass -le $Passes; $pass++) {
      $warmup = if ($pass -eq 1) { $WarmupSeconds } else { 0 }
      Write-Host "Synthetic $scenario pass $pass/$Passes ($DurationSeconds seconds; warm-up $warmup seconds). No production runtime/capture."
      $passPath = Join-Path $output "$scenario-pass-$pass.json"
      & $probe -TargetProcessId $process.Id -ExpectedStartTimeUtc $startTime -WarmupSeconds $warmup `
        -DurationSeconds $DurationSeconds -SampleIntervalMilliseconds $SampleIntervalMilliseconds -RequireFullDuration -OutputPath $passPath | Out-Null
      $measurement = Get-Content -LiteralPath $passPath -Raw | ConvertFrom-Json
      if (-not $measurement.Completed) { throw 'Incomplete pass cannot enter the baseline.' }
      $passesForScenario.Add($measurement)
    }
    $reports.Add([pscustomobject]@{
      Identity = $identity
      Passes = $passesForScenario.ToArray()
      MeanPassAverage = [pscustomobject]@{
        WorkingSetMiB = [math]::Round(($passesForScenario | ForEach-Object { $_.WorkingSetMb.Average } | Measure-Object -Average).Average, 2)
        PrivateMemoryMiB = [math]::Round(($passesForScenario | ForEach-Object { $_.PrivateMemoryMb.Average } | Measure-Object -Average).Average, 2)
        MachineCpuPercent = [math]::Round(($passesForScenario | ForEach-Object { $_.MachineCpuPercent.Average } | Measure-Object -Average).Average, 3)
        Handles = [math]::Round(($passesForScenario | ForEach-Object { $_.HandleCount.Average } | Measure-Object -Average).Average, 1)
      }
    })
  }
  finally {
    # Only the Process instance created above is controlled, never a user's App.
    if (-not $process.HasExited) {
      try { $process.StandardInput.WriteLine('stop'); $process.StandardInput.Close() }
      catch [IO.IOException] { } # An early-exiting owned child may close its pipe.
      if (-not $process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit() }
    }
    $exitCode = $process.ExitCode
    $process.Dispose()
  }
  if ($exitCode -ne 0) { throw 'Synthetic benchmark did not exit cleanly.' }
}

$result = [pscustomobject]@{
  CapturedAtUtc = [datetime]::UtcNow.ToString('o')
  SourceCommit = $commit
  WorkingTreeDirty = $workingTreeDirty
  DotnetSdk = $sdk
  ProductAssemblySha256 = $productHash
  EvidenceKind = 'SyntheticHiddenGuiOnly; not production App/blur/capture/startup/network'
  WarmupSeconds = $WarmupSeconds
  PassDurationSeconds = $DurationSeconds
  SampleIntervalMilliseconds = $SampleIntervalMilliseconds
  Scenarios = $reports.ToArray()
}
$result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
$reports | ForEach-Object { [pscustomobject]@{ Scenario=$_.Identity.Scenario; WorkingSetMiB=$_.MeanPassAverage.WorkingSetMiB; PrivateMemoryMiB=$_.MeanPassAverage.PrivateMemoryMiB; MachineCpuPercent=$_.MeanPassAverage.MachineCpuPercent; Handles=$_.MeanPassAverage.Handles } } | Format-Table
Write-Host "Saved synthetic baseline to $output. This is not live-runtime qualification."
