# Shared observation math; no process, window, settings or network mutation.
function New-SoakAccumulator {
  [pscustomobject]@{ Count = 0L; Sum = 0d; Maximum = [double]::NegativeInfinity }
}

function Add-SoakValue($Accumulator, [double]$Value) {
  if (-not [double]::IsFinite($Value) -or $Value -lt 0) { throw 'Invalid process counter.' }
  $Accumulator.Count++
  $Accumulator.Sum += $Value
  $Accumulator.Maximum = [math]::Max($Accumulator.Maximum, $Value)
}

function Get-SoakMetric($Accumulator) {
  if ($Accumulator.Count -eq 0) { return $null }
  [pscustomobject]@{
    Average = [math]::Round($Accumulator.Sum / $Accumulator.Count, 4)
    Maximum = [math]::Round($Accumulator.Maximum, 4)
  }
}

function Get-SoakGrowth([double]$First, [double]$Last) {
  if ($First -le 0) { return $null }
  [math]::Round(100d * ($Last - $First) / $First, 4)
}

function Get-SoakCompleteness {
  param([double]$ElapsedSeconds, [double]$RequestedSeconds, [long]$Samples,
    [int]$IntervalMilliseconds, [double]$MaximumGapMilliseconds, [bool]$TargetAlive,
    [bool]$ReachedEnd, [bool]$ClockContinuous)
  $coverage = if ($ElapsedSeconds -gt 0) { [math]::Min(1d, $Samples * $IntervalMilliseconds / ($ElapsedSeconds * 1000)) } else { 0d }
  [pscustomobject]@{
    Coverage = [math]::Round($coverage, 4)
    Completed = $ReachedEnd -and $TargetAlive -and $ClockContinuous -and
      $ElapsedSeconds -ge $RequestedSeconds -and $Samples -gt 0 -and
      $coverage -ge 0.8 -and $MaximumGapMilliseconds -le [math]::Max(1000, 5 * $IntervalMilliseconds)
  }
}

function Read-SoakAdmission([string]$Directory) {
  $path = Join-Path $Directory 'admission.json'
  if ((Get-Item -LiteralPath $path).Length -gt 65536) { throw 'Oversized admission metadata.' }
  $value = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
  if ($value.SchemaVersion -ne 1 -or $value.EvidenceKind -cne 'PrivacyMaskProcessSoak' -or
      $value.SessionId -cnotmatch '^[a-f0-9]{32}$' -or $value.BinarySha256 -cnotmatch '^[a-f0-9]{64}$' -or
      $value.Scope -cnotin @('SyntheticProcessObserverTest','FullAppResourceObservation') -or
      $value.RequestedDurationSeconds -lt 2 -or $value.RequestedDurationSeconds -gt 28800 -or
      $value.SampleIntervalMilliseconds -lt 100 -or $value.SampleIntervalMilliseconds -gt 60000) { throw 'Unsupported soak admission.' }
  return $value
}
