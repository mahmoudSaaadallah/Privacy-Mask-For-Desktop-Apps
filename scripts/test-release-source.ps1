$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
$identity = Get-PrivacyMaskVersionIdentity -RepoRoot $repoRoot
$script:sourceChecks = 0
$previousFault = Get-Variable PrivacyMaskSourceTestFault -Scope Global -ErrorAction SilentlyContinue
$global:PrivacyMaskSourceTestFault = ''

# Every git command of the source gate is intercepted; no tags/network/checkout writes.
function git {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CommandArguments)
  $global:LASTEXITCODE = 0
  $command = $CommandArguments -join ' '
  if ($command -eq 'rev-parse HEAD') { return '1' * 40 }
  if ($command.StartsWith('rev-parse refs/tags/')) {
    if ($global:PrivacyMaskSourceTestFault -eq 'missing-tag') { $global:LASTEXITCODE = 128; return }
    if ($global:PrivacyMaskSourceTestFault -eq 'wrong-tag-commit') { return '2' * 40 }
    return '1' * 40
  }
  if ($command -eq 'merge-base --is-ancestor HEAD refs/remotes/origin/main') {
    if ($global:PrivacyMaskSourceTestFault -eq 'unmerged') { $global:LASTEXITCODE = 1 }
    if ($global:PrivacyMaskSourceTestFault -eq 'missing-main') { $global:LASTEXITCODE = 128 }
    return
  }
  if ($command -eq 'status --porcelain --untracked-files=normal') {
    if ($global:PrivacyMaskSourceTestFault -eq 'dirty') { return ' M source-fixture' }
    return
  }
  throw 'Unexpected command in source gate mock.'
}

try {
  & (Join-Path $PSScriptRoot 'assert-release-source.ps1') -ExpectedTag $identity.ExpectedTag
  $script:sourceChecks++
  foreach ($fault in @('missing-tag', 'wrong-tag-commit', 'unmerged', 'missing-main', 'dirty')) {
    $global:PrivacyMaskSourceTestFault = $fault
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'assert-release-source.ps1') -ExpectedTag $identity.ExpectedTag } catch { $rejected = $true }
    if (-not $rejected) { throw "Source gate failed to reject: $fault" }
    $script:sourceChecks++
  }
  $rejected = $false
  try { & (Join-Path $PSScriptRoot 'assert-release-source.ps1') -ExpectedTag 'v99.0.0' } catch { $rejected = $true }
  if (-not $rejected) { throw 'Source gate accepted wrong product/tag identity.' }
  $script:sourceChecks++
  $sourceGate = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'assert-release-source.ps1') -Raw
  if ($sourceGate -notmatch 'Get-PrivacyMaskReleaseFlags[^\r\n]+-PreviewOnly') {
    throw 'Protected source admission lost Preview-only rollout enforcement.'
  }
  $script:sourceChecks++
  Write-Host "Passed $script:sourceChecks source gate checks with mocked git; no actual tags or network changes."
}
finally {
  $global:LASTEXITCODE = 0
  if ($previousFault) { $global:PrivacyMaskSourceTestFault = $previousFault.Value }
  else { Remove-Variable PrivacyMaskSourceTestFault -Scope Global }
}
