Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')

function Invoke-PrivacyMaskGh {
  param([string[]]$CommandArguments)
  $result = & gh @CommandArguments
  if ($LASTEXITCODE -ne 0) { throw 'GitHub release command failed; a draft may remain. No publish fallback is permitted.' }
  return $result
}

function Assert-PrivacyMaskRemoteTag {
  param([string]$Repository, [string]$Tag, [string]$ExpectedCommit)
  $tagRoute = "repos/$Repository/git/ref/tags/$([Uri]::EscapeDataString($Tag))"
  $reference = (Invoke-PrivacyMaskGh @('api', $tagRoute)) | ConvertFrom-Json
  $object = $reference.object
  $depth = 0
  while ($object.type -eq 'tag' -and $depth -lt 4) {
    if ($object.sha -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid GitHub tag object.' }
    $annotated = (Invoke-PrivacyMaskGh @('api', "repos/$Repository/git/tags/$($object.sha)")) | ConvertFrom-Json
    $object = $annotated.object
    $depth++
  }
  if ($object.type -ne 'commit' -or $object.sha -cne $ExpectedCommit) { throw 'GitHub tag does not point to the verified source commit.' }
}

function Get-PrivacyMaskTagReleases {
  param([string]$Repository, [string]$Tag)
  # The tag-name endpoint returns published releases, not drafts. Include all
  # authenticated pages so an earlier draft cannot be hidden past page one.
  $json = (Invoke-PrivacyMaskGh @('api', "repos/$Repository/releases?per_page=100", '--paginate', '--slurp')) -join "`n"
  # A property envelope preserves single/empty nested arrays on PS 5.1 and 7;
  # top-level ConvertFrom-Json pipeline enumeration otherwise flattens one page.
  $envelope = ('{"pages":' + $json + '}') | ConvertFrom-Json
  if ($envelope.pages -isnot [Array]) { throw 'Invalid release-list metadata.' }
  foreach ($page in $envelope.pages) {
    if ($page -isnot [Array]) { throw 'Invalid paginated release metadata.' }
    foreach ($release in $page) {
      if ($release.tag_name -ceq $Tag) { $release }
    }
  }
}

function Publish-PrivacyMaskRelease {
  param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [string]$OutputDirectory = 'artifacts/release'
  )
  if ($Repository -cne 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps' -or $ExpectedCommit -cnotmatch '^[0-9a-f]{40}$') {
    throw 'Release destination/source identity is not supported.'
  }
  $identity = Get-PrivacyMaskVersionIdentity -RepoRoot $RepoRoot
  $flags = @(Get-PrivacyMaskReleaseFlags -RepoRoot $RepoRoot -Tag $Tag)
  $root = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $RepoRoot $OutputDirectory }
  $base = "PrivacyMask-$($identity.Version)-win-x64"
  $names = @("$base.zip", "$base.zip.sha256", "$base-update.zip", 'update-manifest.json', 'update-manifest.sig.json')
  $paths = @($names | ForEach-Object { Join-Path $root $_ })
  $expected = @{}
  foreach ($path in $paths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
      throw 'A required verified release asset is missing or empty.'
    }
    $expected[[IO.Path]::GetFileName($path)] = @{
      size = (Get-Item -LiteralPath $path).Length
      digest = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
  }
  Assert-PrivacyMaskRemoteTag $Repository $Tag $ExpectedCommit
  if (@(Get-PrivacyMaskTagReleases $Repository $Tag).Count -ne 0) {
    throw 'A release or draft already exists for this tag. Inspect it explicitly; automatic retry/resume is refused.'
  }
  $createdUrl = Invoke-PrivacyMaskGh (@('release', 'create', $Tag) + $paths + @('--repo', $Repository, '--draft',
    '--generate-notes', '--title', "PrivacyMask $Tag") + $flags)
  if ($createdUrl -isnot [string] -or -not $createdUrl.StartsWith("https://github.com/$Repository/releases/tag/", [StringComparison]::Ordinal) -or
      $createdUrl -match '\s') { throw 'Missing or invalid created draft URL. Inspect remote state; do not retry blindly.' }
  $matchingReleases = @(Get-PrivacyMaskTagReleases $Repository $Tag)
  if ($matchingReleases.Count -ne 1 -or $matchingReleases[0].html_url -cne $createdUrl -or
      ($matchingReleases[0].id -isnot [int] -and $matchingReleases[0].id -isnot [long]) -or $matchingReleases[0].id -le 0) {
    throw 'Created draft identity is missing or ambiguous. No draft was published.'
  }
  $releaseId = $matchingReleases[0].id
  $releaseRoute = "repos/$Repository/releases/$releaseId"
  $release = (Invoke-PrivacyMaskGh @('api', $releaseRoute)) | ConvertFrom-Json
  if ($release.id -ne $releaseId -or $release.html_url -cne $createdUrl -or
      $release.draft -isnot [bool] -or -not $release.draft -or $release.tag_name -cne $Tag -or
      $release.prerelease -isnot [bool] -or $release.prerelease -ne $identity.IsPrerelease -or @($release.assets).Count -ne $names.Count) {
    throw 'Draft release identity/classification/asset set is inconsistent. It was not published.'
  }
  $seen = @{}
  foreach ($asset in $release.assets) {
    if (-not $expected.ContainsKey($asset.name) -or $seen.ContainsKey($asset.name) -or
        $asset.state -ne 'uploaded' -or $asset.size -ne $expected[$asset.name].size -or $asset.digest -cne $expected[$asset.name].digest) {
      throw 'Uploaded asset digest/size/state differs from verified local files. Draft retained; publication refused.'
    }
    $seen[$asset.name] = $true
  }
  Assert-PrivacyMaskRemoteTag $Repository $Tag $ExpectedCommit
  $latest = if ($identity.IsPrerelease) { 'false' } else { 'true' }
  # Never resolve an edit by tag: duplicate drafts could select another object.
  Invoke-PrivacyMaskGh @('api', '--method', 'PATCH', $releaseRoute, '-F', 'draft=false', '-f', "make_latest=$latest") | Out-Null
  $published = (Invoke-PrivacyMaskGh @('api', $releaseRoute)) | ConvertFrom-Json
  if ($published.id -ne $releaseId -or $published.draft -isnot [bool] -or $published.draft -or $published.tag_name -cne $Tag -or
      $published.prerelease -ne $identity.IsPrerelease) {
    throw 'GitHub did not confirm the expected published release. Inspect remote state; do not retry blindly.'
  }
  Write-Host 'Published verified complete release assets from draft.'
}
