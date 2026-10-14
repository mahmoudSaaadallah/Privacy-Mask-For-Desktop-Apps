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
    return
  }
  if ($CommandArguments[0] -eq 'release' -and $CommandArguments[1] -eq 'edit') { $script:draft = $false; return }
  if ($CommandArguments[0] -ne 'api') { throw 'Unexpected mock command.' }
  if ($CommandArguments[1].Contains('/git/ref/tags/')) {
    $sha = if ($script:fault -eq 'moved-tag' -and $script:created) { '2' * 40 } else { $script:expectedCommit }
    return (@{ object = @{ type = 'commit'; sha = $sha } } | ConvertTo-Json -Depth 8 -Compress)
  }
  $assets = @($script:assetMetadata | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
  if ($script:fault -eq 'wrong-digest') { $assets[0].digest = 'sha256:' + ('0' * 64) }
  if ($script:fault -eq 'missing-digest') { $assets[0].PSObject.Properties.Remove('digest') }
  if ($script:fault -eq 'duplicate-asset') { $assets[1].name = $assets[0].name }
  $tag = if ($script:fault -eq 'wrong-tag') { 'v9.0.0' } else { $script:identity.ExpectedTag }
  $pre = if ($script:fault -eq 'wrong-prerelease') { -not $script:identity.IsPrerelease } else { $script:identity.IsPrerelease }
  return (@{ draft = $script:draft; tag_name = $tag; prerelease = $pre; assets = $assets } | ConvertTo-Json -Depth 8 -Compress)
}

function Prepare-PublicationFixture {
  param([bool]$Stable = $false, [string]$Fault = '')
  $script:fault = $Fault
  $script:draft = $true
  $script:created = $false
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
    Prepare-PublicationFixture -Stable $stable
    Publish-PrivacyMaskRelease -RepoRoot $fixtureRoot -Tag $script:identity.ExpectedTag `
      -Repository 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps' -ExpectedCommit $script:expectedCommit
    $create = @($script:commands | Where-Object { $_[0] -eq 'release' -and $_[1] -eq 'create' })
    $edit = @($script:commands | Where-Object { $_[0] -eq 'release' -and $_[1] -eq 'edit' })
    Assert-Publication ($create.Count -eq 1 -and $create[0] -contains '--draft' -and $create[0] -contains '--verify-tag') 'draft/tag creation'
    Assert-Publication (($create[0] -contains '--prerelease') -eq (-not $stable)) 'beta/stable classification'
    Assert-Publication ($edit.Count -eq 1 -and $edit[0] -contains '--draft=false') 'publication after validation'
    Assert-Publication (($edit[0] -contains '--latest=true') -eq $stable -and ($edit[0] -contains '--latest=false') -eq (-not $stable)) 'latest flag'
    Assert-Publication (-not $script:draft) 'publication confirmed'
  }
  foreach ($fault in @('missing-asset', 'wrong-digest', 'missing-digest', 'duplicate-asset', 'wrong-prerelease', 'wrong-tag', 'moved-tag', 'upload-failure')) {
    Prepare-PublicationFixture -Fault $fault
    $rejected = $false
    try {
      Publish-PrivacyMaskRelease -RepoRoot $fixtureRoot -Tag $script:identity.ExpectedTag `
        -Repository 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps' -ExpectedCommit $script:expectedCommit
    } catch { $rejected = $true }
    Assert-Publication $rejected "rejected $fault"
    Assert-Publication (@($script:commands | Where-Object { $_[0] -eq 'release' -and $_[1] -eq 'edit' }).Count -eq 0) 'failure never published'
  }
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
