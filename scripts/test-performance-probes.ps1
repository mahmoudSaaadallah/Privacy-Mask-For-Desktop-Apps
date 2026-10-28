#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$probe = Join-Path $PSScriptRoot 'measure-runtime.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-performance-test-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$shell = (Get-Process -Id $PID).Path
$script:checks = 0

function Assert-Probe([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
  $script:checks++
}
function Start-Fixture([int]$Milliseconds) {
  $info = [Diagnostics.ProcessStartInfo]::new($shell)
  $info.UseShellExecute = $false
  $info.CreateNoWindow = $true
  $info.RedirectStandardOutput = $true
  foreach ($argument in @('-NoProfile','-Command',"[Console]::WriteLine('READY'); [Threading.Thread]::Sleep($Milliseconds)")) { $info.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::Start($info)
  $ready = $process.StandardOutput.ReadLineAsync()
  if (-not $ready.Wait(10000) -or $ready.Result -cne 'READY') {
    Stop-Fixture $process
    throw 'Owned performance fixture did not become ready.'
  }
  return $process
}
function Stop-Fixture([Diagnostics.Process]$Process) {
  if (-not $Process.HasExited) { $Process.Kill(); $Process.WaitForExit() }
  $Process.Dispose()
}

try {
  $process = Start-Fixture 15000
  try {
    $output = Join-Path $fixtureRoot 'complete.json'
    & $probe -TargetProcessId $process.Id -ExpectedStartTimeUtc $process.StartTime.ToUniversalTime() `
      -WarmupSeconds 1 -DurationSeconds 2 -SampleIntervalMilliseconds 500 -RequireFullDuration -OutputPath $output | Out-Null
    $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    Assert-Probe ($result.Completed -and $result.SampleCount -ge 3) 'Complete measurement did not cover the requested interval.'
    Assert-Probe ($result.RequestedDurationSeconds -eq 2 -and $result.WarmupSeconds -eq 1) 'Warm-up was not separated from samples.'
    Assert-Probe ($result.DurationSeconds -ge 2 -and $result.DurationSeconds -lt 5) 'Warm-up leaked into the sampling duration.'
    Assert-Probe ($result.MemoryUnit.Contains('MiB') -and $result.CpuNormalization.Contains('total')) 'Metric units are ambiguous.'
    Assert-Probe ($result.ProcessId -eq $process.Id -and $result.WindowsVersion) 'Process/OS identity is missing.'
    Assert-Probe ($result.ProductVersion -ne '' -and $result.FileVersion -ne '') 'Binary metadata was not collected.'
    $rejected = $false
    try { & $probe -TargetProcessId $process.Id -ExpectedStartTimeUtc ([datetime]::UtcNow.AddYears(-1)) -DurationSeconds 2 | Out-Null }
    catch { $rejected = $true }
    Assert-Probe $rejected 'Changed process start identity was accepted.'
  }
  finally { Stop-Fixture $process }

  $process = Start-Fixture 1200
  try {
    $partial = Join-Path $fixtureRoot 'partial.json'
    $rejected = $false
    try { & $probe -TargetProcessId $process.Id -DurationSeconds 5 -SampleIntervalMilliseconds 100 -RequireFullDuration -OutputPath $partial | Out-Null }
    catch { $rejected = $true }
    Assert-Probe ($rejected -and -not (Test-Path -LiteralPath $partial)) 'A shortened pass was recorded as a valid baseline.'
  }
  finally { Stop-Fixture $process }

  $process = Start-Fixture 500
  try {
    $rejected = $false
    try { & $probe -TargetProcessId $process.Id -WarmupSeconds 2 -DurationSeconds 2 -RequireFullDuration | Out-Null }
    catch { $rejected = $true }
    Assert-Probe $rejected 'Warm-up process exit was accepted.'
  }
  finally { Stop-Fixture $process }
  Write-Host "Passed $script:checks performance-probe checks using only owned synthetic processes."
}
finally {
  $resolved = [IO.Path]::GetFullPath($fixtureRoot)
  $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
  if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -or
      -not ([IO.Path]::GetFileName($resolved)).StartsWith('PrivacyMask-performance-test-', [StringComparison]::Ordinal)) {
    throw 'Refusing cleanup outside the owned performance fixture.'
  }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
