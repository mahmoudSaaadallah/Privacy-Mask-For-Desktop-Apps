Set-StrictMode -Version Latest

function Get-PrivacyMaskProductVersion {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
  )

  $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
  if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Product version file not found at '$propsPath'."
  }

  [xml]$props = Get-Content -LiteralPath $propsPath -Raw
  $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
  $version = if ($null -eq $versionNode) { '' } else { $versionNode.InnerText.Trim() }

  if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Directory.Build.props contains an invalid product version '$version'."
  }

  return $version
}
