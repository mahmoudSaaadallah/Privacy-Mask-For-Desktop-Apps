$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.ReleaseEnvironment.ps1')
$script:environmentChecks = 0
$script:routes = New-Object 'System.Collections.Generic.List[string]'

function Assert-EnvironmentCheck {
  param([bool]$Condition)
  if (-not $Condition) { throw 'Release environment fixture failed.' }
  $script:environmentChecks++
}

function Invoke-PrivacyMaskEnvironmentRead {
  param([string]$Route)
  $script:routes.Add($Route)
  if ($script:fault -eq 'unavailable') { throw 'private-token-path-must-not-leak' }
  if ($Route.EndsWith('privacy-mask-release')) { return $script:environment }
  if ($Route.EndsWith('/deployment-branch-policies?per_page=100')) { return $script:policies }
  throw 'Unexpected external route.'
}

$repository = 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps'
foreach ($fault in @('none', 'unavailable', 'wrong-name', 'no-review', 'self-review', 'string-boolean',
    'no-reviewers', 'bad-reviewer', 'boolean-reviewer', 'string-count', 'no-branch-policy', 'protected-branches', 'all-branches',
    'broad-tag', 'branch-not-tag', 'missing-type', 'extra-rule', 'incomplete-page', 'duplicate-review')) {
  $script:fault = $fault
  $script:routes.Clear()
  $script:environment = @{
    name = 'privacy-mask-release'
    protection_rules = @(@{ type = 'required_reviewers'; prevent_self_review = $true;
      reviewers = @(@{ type = 'User'; reviewer = @{ id = 123 } }) })
    deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
  }
  $script:policies = @{ total_count = 1; branch_policies = @(@{ name = 'v*'; type = 'tag' }) }
  switch ($fault) {
    'wrong-name' { $script:environment.name = 'other' }
    'no-review' { $script:environment.protection_rules = @() }
    'self-review' { $script:environment.protection_rules[0].prevent_self_review = $false }
    'string-boolean' { $script:environment.protection_rules[0].prevent_self_review = 'true' }
    'no-reviewers' { $script:environment.protection_rules[0].reviewers = @() }
    'bad-reviewer' { $script:environment.protection_rules[0].reviewers[0].reviewer.id = 0 }
    'boolean-reviewer' { $script:environment.protection_rules[0].reviewers[0].reviewer.id = $true }
    'string-count' { $script:policies.total_count = '1' }
    'no-branch-policy' { $script:environment.deployment_branch_policy = $null }
    'protected-branches' { $script:environment.deployment_branch_policy.protected_branches = $true }
    'all-branches' { $script:environment.deployment_branch_policy.custom_branch_policies = $false }
    'broad-tag' { $script:policies.branch_policies[0].name = '*' }
    'branch-not-tag' { $script:policies.branch_policies[0].type = 'branch' }
    'missing-type' { $script:policies.branch_policies[0].Remove('type') }
    'extra-rule' { $script:policies.branch_policies += @{ name = 'main'; type = 'branch' }; $script:policies.total_count = 2 }
    'incomplete-page' { $script:policies.total_count = 101 }
    'duplicate-review' { $script:environment.protection_rules += $script:environment.protection_rules[0] }
  }
  $failed = $false
  try {
    $result = Assert-PrivacyMaskReleaseEnvironment $repository -ReviewPolicy IndependentReview
    Assert-EnvironmentCheck ($fault -eq 'none' -and $result.PreventSelfReview -eq $true)
  }
  catch {
    $failed = $true
    Assert-EnvironmentCheck (-not $_.Exception.Message.Contains('private-token'))
  }
  Assert-EnvironmentCheck ($failed -eq ($fault -ne 'none'))
  Assert-EnvironmentCheck ($script:routes.Count -ge 1 -and $script:routes.Count -le 2)
}
$before = $script:routes.Count
try { Assert-PrivacyMaskReleaseEnvironment 'foreign/repository'; throw 'Expected rejection.' }
catch { Assert-EnvironmentCheck ($_.Exception.Message -like 'Unsupported release repository*') }
Assert-EnvironmentCheck ($script:routes.Count -eq $before)
try { Assert-PrivacyMaskReleaseEnvironment $repository; throw 'Expected rejection.' }
catch { Assert-EnvironmentCheck ($_.Exception.Message -like 'Release review policy is not approved*') }
Assert-EnvironmentCheck ($script:routes.Count -eq $before)
foreach ($soloFault in @('none', 'wrong-owner', 'team', 'multiple', 'self-review-blocked')) {
  $script:fault = 'none'
  $script:environment = @{
    name = 'privacy-mask-release'
    protection_rules = @(@{ type = 'required_reviewers'; prevent_self_review = $false;
      reviewers = @(@{ type = 'User'; reviewer = @{ id = 123; login = 'mahmoudSaaadallah' } }) })
    deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
  }
  $script:policies = @{ total_count = 1; branch_policies = @(@{ name = 'v*'; type = 'tag' }) }
  switch ($soloFault) {
    'wrong-owner' { $script:environment.protection_rules[0].reviewers[0].reviewer.login = 'another-user' }
    'team' { $script:environment.protection_rules[0].reviewers[0].type = 'Team' }
    'multiple' { $script:environment.protection_rules[0].reviewers += $script:environment.protection_rules[0].reviewers[0] }
    'self-review-blocked' { $script:environment.protection_rules[0].prevent_self_review = $true }
  }
  $failed = $false
  try {
    $result = Assert-PrivacyMaskReleaseEnvironment $repository -ReviewPolicy SoloMaintainer
    Assert-EnvironmentCheck ($soloFault -eq 'none' -and -not $result.PreventSelfReview -and $result.RequiredReviewers -eq 1)
  }
  catch { $failed = $true }
  Assert-EnvironmentCheck ($failed -eq ($soloFault -ne 'none'))
}
# Guard the admission topology as well as mocked API metadata. These checks are
# regression assertions, not a substitute for reviewing workflow changes.
$repoRoot = Split-Path -Parent $PSScriptRoot
$workflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/release.yml') -Raw
$preflight = [regex]::Match($workflow, '(?ms)^  validate-release-environment:\s*\r?\n(.*?)^  package-windows:').Groups[1].Value
$package = [regex]::Match($workflow, '(?ms)^  package-windows:\s*\r?\n(.*)$').Groups[1].Value
Assert-EnvironmentCheck ($preflight.Length -gt 0 -and $package.Length -gt 0)
Assert-EnvironmentCheck ($preflight -notmatch '(?m)^\s+environment:')
Assert-EnvironmentCheck ($preflight -notmatch 'secrets\.|contents: write')
Assert-EnvironmentCheck ($preflight -match 'assert-release-source\.ps1' -and $preflight -match 'assert-release-environment\.ps1')
Assert-EnvironmentCheck ($package -match '(?m)^    needs: validate-release-environment\s*$')
Assert-EnvironmentCheck ($package -match '(?m)^    environment: privacy-mask-release\s*$')
$boundary = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PrivacyMask.ReleaseEnvironment.ps1') -Raw
Assert-EnvironmentCheck ($boundary -match 'gh api --hostname github\.com --method GET')
Assert-EnvironmentCheck ($boundary -notmatch '--method (POST|PUT|PATCH|DELETE)')
Write-Host "Passed $script:environmentChecks mocked environment/topology checks; no network, environment, deployment or secret writes."
