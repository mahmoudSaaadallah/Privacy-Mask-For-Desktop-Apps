#requires -Version 7.0
param(
  [string]$OutputDirectory = 'artifacts/release',
  [string]$PolicyPath = 'build/update-release-policy.json',
  [string]$ExpectedTag = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
$identity = Get-PrivacyMaskVersionIdentity -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($ExpectedTag)) { $ExpectedTag = $identity.ExpectedTag }
Get-PrivacyMaskReleaseFlags -RepoRoot $repoRoot -Tag $ExpectedTag | Out-Null
Push-Location $repoRoot
try {
  $commit = git rev-parse HEAD
  if ($LASTEXITCODE -ne 0) { throw 'Cannot determine committed release identity.' }
  foreach ($item in @(
    @{ File = 'update-manifest.json'; Schema = 'docs/contracts/update-manifest.schema.json' },
    @{ File = 'update-manifest.sig.json'; Schema = 'docs/contracts/update-signature.schema.json' }
  )) {
    $json = Get-Content -LiteralPath (Join-Path $OutputDirectory $item.File) -Raw
    if (-not (Test-Json -Json $json -SchemaFile $item.Schema)) { throw 'Update metadata schema rejected release assets.' }
  }
  dotnet tools/PrivacyMask.ReleaseTool/bin/Release/net10.0/PrivacyMask.ReleaseTool.dll verify `
    --output $OutputDirectory --version $identity.Version --file-version $identity.FileVersion `
    --commit $commit --policy $PolicyPath
  if ($LASTEXITCODE -ne 0) { throw 'Update assets failed independent authentication or payload verification.' }
}
finally { Pop-Location }
