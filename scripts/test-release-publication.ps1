$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Release.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-publication-test-' + [Guid]::NewGuid().ToString('N'))
$script:publicationChecks = 0
$script:expectedCommit = '1' * 40
$script:commands = New-Object 'System.Collections.Generic.List[object]'

function Assert-Publication {
  param([bool]$Condition, [string]$Label)
  if (-not $Condition) { throw "Release publication check failed: $Label" }
  $script:publicationChecks++
}

# Override the sole network boundary. This suite never invokes gh or GitHub.
function Invoke-PrivacyMaskGh {
  param([string[]]$CommandArguments)
  $script:commands.Add($CommandArguments)
  if ($CommandArguments[0] -eq 'release' -and $CommandArguments[1] -eq 'create') {
    if ($script:fault -eq 'upload-failure') { throw 'Synthetic upload failure.' }
    $script:created = $true
    if ($script:fault -eq 'missing-created-url') { return }
    if ($script:fault -eq 'foreign-created-url') { return 'https://example.invalid/releases/tag/fixture' }
    return $script:createdUrl
  }
  if ($CommandArguments[0] -ne 'api') { throw 'Unexpected mock command.' }
  if ($CommandArguments -contains 'PATCH') {
    if ($CommandArguments[3] -cne $script:releaseRoute -or $CommandArguments -notcontains 'draft=false') {
      throw 'Publication must use the exact numeric release ID and Boolean draft field.'
    }
    $script:draft = $false
    return
  }
  if ($CommandArguments[1].Contains('/git/ref/tags/')) {
    $sha = if ($script:fault -eq 'moved-tag' -and $script:created) { '2' * 40 } else { $script:expectedCommit }
    return (@{ object = @{ type = 'commit'; sha = $sha } } | ConvertTo-Json -Depth 8 -Compress)
  }
  # Model GitHub's real distinction: this endpoint never finds an unpublished
  # draft. The old permissive mock accidentally hid the production HTTP 404.
  if ($CommandArguments[1].Contains('/releases/tags/')) { throw 'Synthetic HTTP 404: draft is not a published tag release.' }
  if ($CommandArguments[1].EndsWith('/releases?per_page=100')) {
    if ($CommandArguments -notcontains '--paginate' -or $CommandArguments -notcontains '--slurp') {
      throw 'Release discovery must include all authenticated pages.'
    }
    if ($script:fault -eq 'list-failure' -or ($script:fault -eq 'after-create-list-failure' -and $script:created)) {
      throw 'Synthetic release-list failure.'
    }
    if ($script:fault -eq 'invalid-list-root') { return 'null' }
    if ($script:fault -eq 'invalid-list-page') { return '[{"message":"not a page"}]' }
    $listed = @()
    if ($script:created -or $script:fault.StartsWith('existing-')) {
      $listId = if ($script:fault -eq 'string-id') { '101' } elseif ($script:fault -eq 'zero-id') { 0 } else { 101 }
      $url = if ($script:fault -eq 'wrong-listed-url') { $script:createdUrl + '-other' } else { $script:createdUrl }
      $listed += @{ id = $listId; tag_name = $script:identity.ExpectedTag; html_url = $url }
      if ($script:fault -eq 'ambiguous-draft' -or $script:fault -eq 'existing-duplicate') { $listed += $listed[0] }
      if ($script:fault -eq 'draft-not-listed') { $listed = @() }
    }
    $unrelated = @(@{ id = 77; tag_name = 'v0.0.0'; html_url = $script:createdUrl + '-unrelated' })
    if ($script:singlePage) { return ConvertTo-Json -InputObject (, $listed) -Depth 8 -Compress }
    # The matching release is on the second page, not merely a first-page fixture.
    return ConvertTo-Json -InputObject @($unrelated, $listed) -Depth 8 -Compress
  }
  if ($CommandArguments[1] -cne $script:releaseRoute) { throw 'Unexpected release lookup; numeric ID required.' }
  # Windows PowerShell 5.1 emits a top-level parsed JSON array as one pipeline
  # item. Clone inside an object so the fixture models actual release API shape.
  $assetClone = @{ assets = $script:assetMetadata } | ConvertTo-Json -Depth 8 | ConvertFrom-Json
  $assets = @($assetClone.assets)
  if ($script:fault -eq 'wrong-digest') { $assets[0].digest = 'sha256:' + ('0' * 64) }
  if ($script:fault -eq 'missing-digest') { $assets[0].PSObject.Properties.Remove('digest') }
  if ($script:fault -eq 'duplicate-asset') { $assets[1].name = $assets[0].name }
  $tag = if ($script:fault -eq 'wrong-tag') { 'v9.0.0' } else { $script:identity.ExpectedTag }
  $pre = if ($script:fault -eq 'wrong-prerelease') { -not $script:identity.IsPrerelease } else { $script:identity.IsPrerelease }
  $id = if ($script:fault -eq 'wrong-get-id' -or ($script:fault -eq 'wrong-published-id' -and -not $script:draft)) { 102 } else { 101 }
  return (@{ id = $id; html_url = $script:createdUrl; draft = $script:draft; tag_name = $tag;
    prerelease = $pre; assets = $assets } | ConvertTo-Json -Depth 8 -Compress)
}

function Prepare-PublicationFixture {
  param([bool]$Stable = $false, [string]$Fault = '', [switch]$SinglePage)
  $script:fault = $Fault
  $script:singlePage = [bool]$SinglePage
  $script:draft = $true
  $script:created = $false
  $script:createdUrl = 'https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases/tag/untagged-fixture'
  $script:releaseRoute = 'repos/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases/101'
  $script:commands.Clear()
  [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
  if ($Stable) { $props.Project.PropertyGroup.Version = ($props.Project.PropertyGroup.Version -split '-')[0] }
  $props.Save((Join-Path $fixtureRoot 'Directory.Build.props'))
  $script:identity = Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot
  $output = Join-Path $fixtureRoot 'artifacts/release'
  New-Item -ItemType Directory -Path $output -Force | Out-Null
  $base = "PrivacyMask-$($script:identity.Version)-win-x64"
  $script:assetMetadata = @()
  foreach ($name in @("$base.zip", "$base.zip.sha256", "$base-update.zip", 'update-manifest.json', 'update-manifest.sig.json')) {
    $path = Join-Path $output $name
    [IO.File]::WriteAllText($path, 'synthetic-publication-fixture-' + $name)
    $script:assetMetadata += @{ name = $name; state = 'uploaded'; size = (Get-Item $path).Length;
      digest = 'sha256:' + (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }
  }
  if ($Fault -eq 'missing-asset') { Remove-Item -LiteralPath (Join-Path $output 'update-manifest.sig.json') }
}

try {
  New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
  foreach ($stable in @($false, $true)) {
    foreach ($singlePage in @($false, $true)) {
      Prepare-PublicationFixture -Stable $stable -SinglePage:$singlePage
      Publish-PrivacyMaskRelease -RepoRoot $fixtureRoot -Tag $script:identity.ExpectedTag `
        -Repository 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps' -ExpectedCommit $script:expectedCommit
      $create = @($script:commands | Where-Object { $_[0] -eq 'release' -and $_[1] -eq 'create' })
      $edit = @($script:commands | Where-Object { $_ -contains 'PATCH' })
      Assert-Publication ($create.Count -eq 1 -and $create[0] -contains '--draft' -and $create[0] -contains '--verify-tag') 'draft/tag creation'
      Assert-Publication (($create[0] -contains '--prerelease') -eq (-not $stable)) 'beta/stable classification'
      Assert-Publication ($edit.Count -eq 1 -and $edit[0][3] -ceq $script:releaseRoute -and $edit[0] -contains 'draft=false') 'publication by validated numeric ID'
      Assert-Publication (($edit[0] -contains 'make_latest=true') -eq $stable -and ($edit[0] -contains 'make_latest=false') -eq (-not $stable)) 'latest flag'
      Assert-Publication (-not $script:draft) 'publication confirmed'
      Assert-Publication (@($script:commands | Where-Object { $_[0] -eq 'api' -and $_[1].Contains('/releases/tags/') }).Count -eq 0) 'no published-only tag endpoint'
      Assert-Publication (@($script:commands | Where-Object { $_[0] -eq 'release' -and $_[1] -eq 'edit' }).Count -eq 0) 'no ambiguous tag-based edit'
    }
  }
  foreach ($fault in @('missing-asset', 'wrong-digest', 'missing-digest', 'duplicate-asset', 'wrong-prerelease', 'wrong-tag', 'moved-tag', 'upload-failure',
    'missing-created-url', 'foreign-created-url', 'wrong-listed-url', 'string-id', 'zero-id', 'wrong-get-id', 'ambiguous-draft', 'draft-not-listed',
    'list-failure', 'invalid-list-root', 'invalid-list-page', 'after-create-list-failure', 'existing-draft', 'existing-published', 'existing-duplicate')) {
    Prepare-PublicationFixture -Fault $fault
    $rejected = $false
    try {
      Publish-PrivacyMaskRelease -RepoRoot $fixtureRoot -Tag $script:identity.ExpectedTag `
        -Repository 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps' -ExpectedCommit $script:expectedCommit
    } catch { $rejected = $true }
    Assert-Publication $rejected "rejected $fault"
    Assert-Publication (@($script:commands | Where-Object { $_ -contains 'PATCH' }).Count -eq 0) 'failure never published'
    if ($fault.StartsWith('existing-') -or $fault -eq 'list-failure' -or $fault.StartsWith('invalid-list-')) {
      Assert-Publication (-not $script:created) 'pre-existing or unreadable state never created another draft'
    }
  }
  Prepare-PublicationFixture -Fault 'wrong-published-id'
  $rejected = $false
  try {
    Publish-PrivacyMaskRelease -RepoRoot $fixtureRoot -Tag $script:identity.ExpectedTag `
      -Repository 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps' -ExpectedCommit $script:expectedCommit
  } catch { $rejected = $true }
  Assert-Publication $rejected 'post-publication identity mismatch requires inspection'
  Assert-Publication (@($script:commands | Where-Object { $_ -contains 'PATCH' }).Count -eq 1) 'uncertain publication never retried'
  Write-Host "Passed $script:publicationChecks mocked draft publication checks; no network, tags or releases created."
}
finally {
  $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
  $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
  if ($resolvedFixture.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
      (Split-Path -Leaf $resolvedFixture).StartsWith('PrivacyMask-publication-test-') -and (Test-Path -LiteralPath $resolvedFixture)) {
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
  }
}
