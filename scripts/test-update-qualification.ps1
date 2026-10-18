$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Qualification.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-qualification-test-' + [Guid]::NewGuid().ToString('N'))
$script:qualificationChecks = 0

function Assert-Qualification {
  param([bool]$Condition, [string]$Label)
  if (-not $Condition) { throw "Qualification fixture failed: $Label" }
  $script:qualificationChecks++
}
function Expect-QualificationFailure {
  param([scriptblock]$Action)
  $failed = $false
  try { & $Action | Out-Null } catch { $failed = $true }
  Assert-Qualification $failed 'expected safe refusal'
}
try {
  New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'docs/templates') | Out-Null
  New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'build') | Out-Null
  Copy-Item -LiteralPath (Join-Path $repoRoot 'build/update-signing-approval.json') -Destination (Join-Path $fixtureRoot 'build')
  Copy-Item -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Destination $fixtureRoot
  Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/templates/update-qualification-plan.json') -Destination (Join-Path $fixtureRoot 'docs/templates')
  Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/update-qualification-runbook.md') -Destination (Join-Path $fixtureRoot 'docs')
  $commit = '1' * 40
  $path = New-PrivacyMaskQualificationPacket $fixtureRoot $commit 'sample'
  Assert-Qualification ($path -eq (Join-Path $fixtureRoot 'artifacts/update-qualification/packet-sample')) 'fixed output root'
  Assert-Qualification (@(Get-ChildItem -LiteralPath $path -File).Count -eq 2) 'exact two-file packet'
  $evidencePath = Join-Path $path 'evidence.json'
  $report = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
  Assert-Qualification ($report.overallStatus -ceq 'NotRun') 'no fabricated pass'
  Assert-Qualification ($report.sourceCommit -ceq $commit) 'committed source identity'
  Assert-Qualification ($report.plannedReviewPolicy -ceq 'SoloMaintainer') 'planned solo policy without live deployment approval'
  $identity = Get-PrivacyMaskVersionIdentity $fixtureRoot
  Assert-Qualification ($report.sourceVersion -ceq $identity.Version -and $report.sourceFileVersion -ceq $identity.FileVersion) 'unchanged product version'
  Assert-Qualification (@($report.environments).Count -eq 2) 'separate required OS targets'
  foreach ($environment in $report.environments) {
    Assert-Qualification ($environment.id -cin @('windows-10-x64', 'windows-11-x64')) 'supported lab target'
    Assert-Qualification ($null -eq $environment.osBuild -and $null -eq $environment.standardUser -and $null -eq $environment.snapshot) 'VM not falsely provisioned'
    Assert-Qualification ($null -eq $environment.baselineVersion -and $null -eq $environment.targetVersion -and
      $null -eq $environment.approvedPublicKeyFingerprint) 'no invented artifact or approved key'
    Assert-Qualification (@($environment.results).Count -eq 25) 'complete case inventory'
    foreach ($case in $environment.results) {
      Assert-Qualification ($case.status -ceq 'NotRun' -and $null -eq $case.evidence) 'pending actual evidence'
    }
  }
  foreach ($approval in $report.approvals.PSObject.Properties) {
    Assert-Qualification ($approval.Value -ceq 'Pending') 'owner approval remains pending'
  }
  Assert-Qualification ((Get-FileHash -LiteralPath (Join-Path $path 'RUNBOOK.md')).Hash -eq
    (Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'docs/update-qualification-runbook.md')).Hash) 'exact runbook copy'
  $before = (Get-FileHash -LiteralPath $evidencePath).Hash
  Expect-QualificationFailure { New-PrivacyMaskQualificationPacket $fixtureRoot $commit 'sample' }
  Assert-Qualification ((Get-FileHash -LiteralPath $evidencePath).Hash -eq $before) 'existing packet preserved'
  foreach ($badName in @('../escape', '..', 'C:\escape', 'MixedCase', 'a/b', ('x' * 49))) {
    Expect-QualificationFailure { New-PrivacyMaskQualificationPacket $fixtureRoot $commit $badName }
  }
  Expect-QualificationFailure { New-PrivacyMaskQualificationPacket $fixtureRoot 'not-a-source-commit' 'bad-source' }
  $templatePath = Join-Path $fixtureRoot 'docs/templates/update-qualification-plan.json'
  $plan = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
  $plan.cases[0] | Add-Member -NotePropertyName status -NotePropertyValue 'Pass'
  $plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $templatePath -Encoding UTF8
  $fresh = New-PrivacyMaskQualificationPacket $fixtureRoot $commit 'extra-status'
  $freshReport = Get-Content -LiteralPath (Join-Path $fresh 'evidence.json') -Raw | ConvertFrom-Json
  Assert-Qualification ($freshReport.environments[0].results[0].status -ceq 'NotRun') 'template cannot preapprove a result'
  $plan.cases[1].id = $plan.cases[0].id
  $plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $templatePath -Encoding UTF8
  Expect-QualificationFailure { New-PrivacyMaskQualificationPacket $fixtureRoot $commit 'duplicate-case' }
  Assert-Qualification (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'artifacts/update-qualification/packet-duplicate-case'))) 'reject plan before output'
  Write-Host "Passed $script:qualificationChecks qualification packet checks; no VM, network, key, App or user installation used."
}
finally {
  $resolved = [IO.Path]::GetFullPath($fixtureRoot)
  $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
  if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -or
      [IO.Path]::GetFileName($resolved) -notmatch '^PrivacyMask-qualification-test-[0-9a-f]{32}$') {
    throw 'Refused cleanup outside the exact temporary fixture.'
  }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
