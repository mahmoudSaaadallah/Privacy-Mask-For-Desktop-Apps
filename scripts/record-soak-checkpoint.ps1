#requires -Version 7.0
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$SessionDirectory,
  [Parameter(Mandatory = $true)]
  [ValidateSet('TrayIdle','OneWindow','TwoWindows','MovementResize','Hover','AltExclusive','PanicPin','SettingsCycles','PauseCycles','MatchingEndpoints')][string]$Check,
  [Parameter(Mandatory = $true)][ValidateSet('Pass','Fail','NotRun')][string]$Result,
  [ValidateRange(0,10000)][int]$Cycles = 0
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.Soak.ps1')
$admission = Read-SoakAdmission $SessionDirectory
$Check = @('TrayIdle','OneWindow','TwoWindows','MovementResize','Hover','AltExclusive','PanicPin','SettingsCycles','PauseCycles','MatchingEndpoints') | Where-Object { $_ -eq $Check }
$Result = @('Pass','Fail','NotRun') | Where-Object { $_ -eq $Result }
if ($Check -in @('SettingsCycles','PauseCycles') -and $Result -eq 'Pass' -and $Cycles -lt 25) {
  throw 'A passing cycle checkpoint needs at least 25 completed cycles.'
}
if ($Check -notin @('SettingsCycles','PauseCycles') -and $Cycles -ne 0) { throw 'Cycle count belongs only to cycle checks.' }
$event = [pscustomobject]@{
  SessionId = $admission.SessionId; RecordedAtUtc = [datetime]::UtcNow.ToString('o')
  Source = 'ManualOperatorReport'; Check = $Check; Result = $Result; Cycles = $Cycles
}
# No free text: never request/store a PIN, profile name, executable path or title.
$stream = [IO.File]::Open((Join-Path $SessionDirectory 'checkpoints.jsonl'), [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
  $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
  try { $writer.WriteLine(($event | ConvertTo-Json -Compress)) } finally { $writer.Dispose() }
}
finally { $stream.Dispose() }
Write-Host "Recorded manual $Check = $Result. This does not qualify the build automatically."
