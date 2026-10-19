Set-StrictMode -Version Latest

function Get-PrivacyMaskVersionIdentity {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
  )

  $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
  if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Product version file not found at '$propsPath'."
  }

  [xml]$props = Get-Content -LiteralPath $propsPath -Raw
  $values = @{}
  foreach ($name in @('Version', 'AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
    $nodes = $props.SelectNodes("/Project/PropertyGroup/$name")
    if ($nodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($nodes[0].InnerText)) {
      throw "Directory.Build.props must contain exactly one explicit $name."
    }
    $values[$name] = $nodes[0].InnerText.Trim()
  }

  $version = $values['Version']
  $versionMatch = [Regex]::Match($version,
    '^(?<Core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?:-(?<Prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$')
  if (-not $versionMatch.Success) {
    throw "Directory.Build.props contains an invalid product version '$version'."
  }
  $core = $versionMatch.Groups['Core'].Value
  $prerelease = $versionMatch.Groups['Prerelease'].Value
  foreach ($identifier in $prerelease.Split('.')) {
    if ($identifier -match '^[0-9]+$' -and $identifier.Length -gt 1 -and $identifier.StartsWith('0')) {
      throw "Numeric prerelease identifiers cannot have leading zeroes: '$version'."
    }
  }

  foreach ($name in @('AssemblyVersion', 'FileVersion')) {
    $numeric = $values[$name]
    if ($numeric -notmatch '^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$') {
      throw "$name must be a four-part Windows numeric version, not '$numeric'."
    }
    foreach ($part in $numeric.Split('.')) {
      if ([UInt64]$part -gt 65534) { throw "$name components must be between 0 and 65534." }
    }
    if (($numeric.Split('.')[0..2] -join '.') -ne $core) {
      throw "$name '$numeric' does not match product version core '$core'."
    }
  }
  if ($values['InformationalVersion'] -ne $version -and $values['InformationalVersion'] -ne '$(Version)') {
    throw 'InformationalVersion must equal Version or reference $(Version).'
  }

  return [PSCustomObject]@{
    Version = $version
    CoreVersion = $core
    AssemblyVersion = $values['AssemblyVersion']
    FileVersion = $values['FileVersion']
    IsPrerelease = $versionMatch.Groups['Prerelease'].Success
    ExpectedTag = "v$version"
  }
}

function Get-PrivacyMaskProductVersion {
  param([Parameter(Mandatory = $true)][string]$RepoRoot)
  return (Get-PrivacyMaskVersionIdentity -RepoRoot $RepoRoot).Version
}

function Get-PrivacyMaskReleaseFlags {
  param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$Tag,
    [switch]$PreviewOnly
  )
  $identity = Get-PrivacyMaskVersionIdentity -RepoRoot $RepoRoot
  if ($Tag -cne $identity.ExpectedTag) {
    throw "Release tag '$Tag' does not match '$($identity.ExpectedTag)'."
  }
  if ($PreviewOnly -and -not $identity.IsPrerelease) {
    throw 'This updater rollout permits Preview releases only. Stable publication needs a separately reviewed qualification change.'
  }
  # The workflow already runs on an existing pushed tag. Never let gh invent one.
  '--verify-tag'
  if ($identity.IsPrerelease) {
    '--prerelease'
    '--latest=false'
  }
}

function Assert-PrivacyMaskPackageVersion {
  param(
    [Parameter(Mandatory = $true)]$Identity,
    [Parameter(Mandatory = $true)][string]$FileVersion,
    [Parameter(Mandatory = $true)][string]$ProductVersion
  )
  if ($FileVersion -cne $Identity.FileVersion) {
    throw "Packaged executable version '$FileVersion' does not match '$($Identity.FileVersion)'."
  }
  if ([string]::IsNullOrWhiteSpace($ProductVersion) -or $ProductVersion.Split('+')[0] -cne $Identity.Version) {
    throw "Packaged product version '$ProductVersion' does not match '$($Identity.Version)'."
  }
}

