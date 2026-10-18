Set-StrictMode -Version Latest

function Invoke-PrivacyMaskEnvironmentRead {
  param([Parameter(Mandatory = $true)][string]$Route)
  # No creation, secret reads, secret values or deployment writes. Fixed host and GET only.
  $response = & gh api --hostname github.com --method GET -H 'Accept: application/vnd.github+json' $Route 2>$null
  if ($LASTEXITCODE -ne 0) { throw 'Release environment could not be verified. No protected signing job may proceed.' }
  $json = $response -join "`n"
  if ($json.Length -gt 1MB) { throw 'Release environment metadata exceeded its reviewed budget.' }
  try { return ($json | ConvertFrom-Json -ErrorAction Stop) }
  catch { throw 'Release environment metadata is invalid.' }
}

function Assert-PrivacyMaskReleaseEnvironment {
  param([Parameter(Mandatory = $true)][string]$Repository, [string]$ReviewPolicy = 'Unapproved')
  if ($Repository -cne 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps') {
    throw 'Unsupported release repository. No environment lookup was made.'
  }
  if ($ReviewPolicy -cnotin @('IndependentReview', 'SoloMaintainer')) {
    throw 'Release review policy is not approved. No environment lookup was made.'
  }
  $route = "repos/$Repository/environments/privacy-mask-release"
  try {
    $environment = Invoke-PrivacyMaskEnvironmentRead $route
    if ($environment.name -cne 'privacy-mask-release') { throw 'identity' }
    $review = @($environment.protection_rules | Where-Object { $_.type -ceq 'required_reviewers' })
    $preventSelfReview = $ReviewPolicy -ceq 'IndependentReview'
    if ($review.Count -ne 1 -or $review[0].prevent_self_review -isnot [bool] -or
        $review[0].prevent_self_review -ne $preventSelfReview) { throw 'review protection' }
    $reviewers = @($review[0].reviewers)
    if ($reviewers.Count -lt 1 -or $reviewers.Count -gt 6) { throw 'reviewers' }
    foreach ($reviewer in $reviewers) {
      if ($reviewer.type -cnotin @('User', 'Team') -or
          ($reviewer.reviewer.id -isnot [int] -and $reviewer.reviewer.id -isnot [long]) -or
          $reviewer.reviewer.id -le 0) {
        throw 'reviewer identity'
      }
    }
    if ($ReviewPolicy -ceq 'SoloMaintainer' -and ($reviewers.Count -ne 1 -or
        $reviewers[0].type -cne 'User' -or $reviewers[0].reviewer.login -ine $Repository.Split('/')[0])) {
      throw 'solo owner review'
    }
    $branch = $environment.deployment_branch_policy
    if ($branch.custom_branch_policies -isnot [bool] -or -not $branch.custom_branch_policies -or
        $branch.protected_branches -isnot [bool] -or $branch.protected_branches) { throw 'tag restrictions' }
    $policies = Invoke-PrivacyMaskEnvironmentRead "$route/deployment-branch-policies?per_page=100"
    $rules = @($policies.branch_policies)
    # Exact reviewed allowlist: no branch rule, broad '*', missing type or incomplete pagination.
    if (($policies.total_count -isnot [int] -and $policies.total_count -isnot [long]) -or
        $policies.total_count -ne 1 -or $rules.Count -ne 1 -or $rules[0].type -cne 'tag' -or
        $rules[0].name -cne 'v*') { throw 'tag allowlist' }
  }
  catch {
    # Never echo remote bodies, reviewer identities, tokens or raw command exceptions.
    throw 'Release environment is missing, inaccessible or lacks required review/self-review/tag protections. Signing remains blocked.'
  }
  [pscustomobject]@{
    Environment = 'privacy-mask-release'
    RequiredReviewers = $reviewers.Count
    ReviewPolicy = $ReviewPolicy
    PreventSelfReview = $preventSelfReview
    AllowedRefType = 'tag'
    AllowedRefPattern = 'v*'
    Scope = 'Configuration only, not per-run approval, key custody, admin-bypass proof or qualification. SoloMaintainer is not independent review.'
  }
}
