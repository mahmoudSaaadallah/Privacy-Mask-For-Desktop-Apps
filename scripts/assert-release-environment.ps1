param([Parameter(Mandatory = $true)][string]$Repository)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PrivacyMask.ReleaseEnvironment.ps1')
$policyPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'build/update-signing-approval.json'
if ((Get-Item -LiteralPath $policyPath).Length -gt 4096) { throw 'Invalid release review policy size.' }
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ($policy.schemaVersion -ne 1) { throw 'Unsupported release review policy.' }
Assert-PrivacyMaskReleaseEnvironment -Repository $Repository -ReviewPolicy $policy.reviewPolicy
