#requires -Version 7.4
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$TrialDirectory,
  [Parameter(Mandatory=$true)][string]$CompressedFirstDirectory,
  [Parameter(Mandatory=$true)][string]$UncompressedDirectory,
  [Parameter(Mandatory=$true)][string]$CompressedRestoredDirectory,
  [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.PackagingTrial.ps1')
if (Test-Path -LiteralPath $OutputPath) { throw 'Use a new comparison output path.' }
$trial=Read-PackagingTrial $TrialDirectory
$hash=(Get-FileHash -LiteralPath (Join-Path $TrialDirectory 'trial.json') -Algorithm SHA256).Hash
$first=Read-PackagingTrialRun $CompressedFirstDirectory $trial $hash
$candidate=Read-PackagingTrialRun $UncompressedDirectory $trial $hash
$restored=Read-PackagingTrialRun $CompressedRestoredDirectory $trial $hash
$report=Get-PackagingTrialComparison $first $candidate $restored
$report|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $OutputPath -Encoding utf8 -NoClobber
$report.Means|Format-Table
Write-Host 'Comparison is a short owner-declared full-App observation; qualification remains NotEvaluated.'
