#requires -Version 7.0
param(
  [string]$OutputDirectory = 'artifacts/release',
  [string]$PolicyPath = 'build/update-release-policy.json',
  [Parameter(Mandatory = $true)][string]$KeyId,
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
  if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'A committed Git source identity is required.' }
  $dirty = git status --porcelain --untracked-files=normal
  if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Signed release packaging requires a clean source checkout.' }
  $settingsSource = Get-Content -LiteralPath 'src/PrivacyMask.Core/Models/AppSettings.cs' -Raw
  $schemaMatch = [regex]::Match($settingsSource, 'public const int CurrentVersion = (?<Version>[0-9]+);')
  $policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
  if (-not $schemaMatch.Success -or $policy.settingsCompatibility.outputSchema -ne [int]$schemaMatch.Groups['Version'].Value) {
    throw 'Release policy settings schema does not match the source.'
  }
  dotnet build tools/PrivacyMask.ReleaseTool/PrivacyMask.ReleaseTool.csproj --configuration Release --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Release tooling build failed.' }
  dotnet tools/PrivacyMask.ReleaseTool/bin/Release/net10.0/PrivacyMask.ReleaseTool.dll pack `
    --exe desktop-app/windows/win-x64/single-file/PrivacyMask.App.exe `
    --output $OutputDirectory --version $identity.Version --file-version $identity.FileVersion `
    --commit $commit --policy $PolicyPath --key-id $KeyId
  if ($LASTEXITCODE -ne 0) { throw 'Signed update packaging failed. No unsigned fallback is permitted.' }
}
finally { Pop-Location }
