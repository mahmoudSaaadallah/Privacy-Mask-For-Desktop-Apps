param([Parameter(Mandatory = $true)][string]$ExpectedTag)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
Get-PrivacyMaskReleaseFlags -RepoRoot $repoRoot -Tag $ExpectedTag | Out-Null
Push-Location $repoRoot
try {
  $commit = git rev-parse HEAD
  if ($LASTEXITCODE -ne 0) { throw 'Missing release commit.' }
  $tagCommit = git rev-parse "refs/tags/${ExpectedTag}^{}"
  if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $commit) { throw 'Checkout differs from the approved version tag.' }
  git merge-base --is-ancestor HEAD refs/remotes/origin/main
  if ($LASTEXITCODE -ne 0) { throw 'Release commit must already be merged into origin/main.' }
  $dirty = git status --porcelain --untracked-files=normal
  if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Release source must be clean.' }
}
finally { Pop-Location }
