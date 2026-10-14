#requires -Version 7.0
param([string]$PublishedExecutable = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
$identity = Get-PrivacyMaskVersionIdentity -RepoRoot $repoRoot
if (-not $PublishedExecutable) { $PublishedExecutable = Join-Path $repoRoot 'desktop-app/windows/win-x64/single-file/PrivacyMask.App.exe' }
$tool = Join-Path $repoRoot 'tools/PrivacyMask.ReleaseTool/bin/Release/net10.0/PrivacyMask.ReleaseTool.dll'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-signed-update-test-' + [Guid]::NewGuid().ToString('N'))
$previousSecret = $env:PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$script:updateChecks = 0

function Assert-UpdateCheck {
  param([bool]$Condition, [string]$Label)
  if (-not $Condition) { throw "Signed update check failed: $Label" }
  $script:updateChecks++
}
function Invoke-TestTool {
  param([string[]]$ToolArguments, [switch]$ExpectFailure)
  $toolOutput = & dotnet $tool @ToolArguments 2>&1
  $toolExit = $LASTEXITCODE
  Assert-UpdateCheck (($toolExit -eq 0) -ne [bool]$ExpectFailure) 'expected tool exit'
  Assert-UpdateCheck (-not (($toolOutput -join "`n").Contains('test-only-secret-marker'))) 'secret redaction'
}

Push-Location $repoRoot
try {
  $commit = git rev-parse HEAD
  if ($LASTEXITCODE -ne 0) { throw 'Test needs a Git source identity.' }
  dotnet build tools/PrivacyMask.ReleaseTool/PrivacyMask.ReleaseTool.csproj --configuration Release --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Release tooling build failed.' }
  New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
  $policyPath = Join-Path $fixtureRoot 'test-policy.json'
  $policy = [ordered]@{
    schemaVersion = 1; minimumWindowsBuild = 19041; minimumUpdaterProtocol = 1
    settingsCompatibility = @{ minimumReadableSchema = 1; maximumReadableSchema = 12; outputSchema = 12 }
    trustedKeys = @(@{ keyId = 'ephemeral-test-key'; publicKeySpkiBase64 = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo()) })
  }
  # Only a PUBLIC test policy is written. The ephemeral private key never goes to disk.
  [IO.File]::WriteAllText($policyPath, ($policy | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
  $unconfiguredPolicyPath = Join-Path $fixtureRoot 'unconfigured-policy.json'
  $unconfiguredPolicy = ($policy | ConvertTo-Json -Depth 8) | ConvertFrom-Json
  $unconfiguredPolicy.minimumWindowsBuild = $null
  $unconfiguredPolicy.trustedKeys = @()
  [IO.File]::WriteAllText($unconfiguredPolicyPath, ($unconfiguredPolicy | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
  $env:PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM = $key.ExportPkcs8PrivateKeyPem()
  $output = Join-Path $fixtureRoot 'valid'
  $common = @('--output', $output, '--version', $identity.Version, '--file-version', $identity.FileVersion, '--commit', $commit, '--policy', $policyPath)
  Invoke-TestTool (@('pack', '--exe', $PublishedExecutable, '--key-id', 'ephemeral-test-key') + $common)
  Invoke-TestTool (@('verify') + $common)
  foreach ($item in @(
    @{ File = 'update-manifest.json'; Schema = 'docs/contracts/update-manifest.schema.json' },
    @{ File = 'update-manifest.sig.json'; Schema = 'docs/contracts/update-signature.schema.json' }
  )) {
    Assert-UpdateCheck (Test-Json -Json (Get-Content (Join-Path $output $item.File) -Raw) -SchemaFile (Join-Path $repoRoot $item.Schema)) 'real metadata schema'
  }
  $archivePath = Join-Path $output "PrivacyMask-$($identity.Version)-win-x64-update.zip"
  $originalHash = (Get-FileHash $archivePath -Algorithm SHA256).Hash
  Invoke-TestTool (@('pack', '--exe', $PublishedExecutable, '--key-id', 'ephemeral-test-key') + $common) -ExpectFailure
  Assert-UpdateCheck ((Get-FileHash $archivePath -Algorithm SHA256).Hash -ceq $originalHash) 'existing asset was not overwritten/deleted'
  $manifestPath = Join-Path $output 'update-manifest.json'
  $originalManifest = [IO.File]::ReadAllBytes($manifestPath)
  [IO.File]::WriteAllBytes($manifestPath, [byte[]]($originalManifest + 10))
  Invoke-TestTool (@('verify') + $common) -ExpectFailure
  [IO.File]::WriteAllBytes($manifestPath, $originalManifest)
  $stream = [IO.File]::Open($archivePath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
  $originalLength = $stream.Length
  try { $stream.Position = $stream.Length; $stream.WriteByte(1) } finally { $stream.Dispose() }
  Invoke-TestTool (@('verify') + $common) -ExpectFailure
  $stream = [IO.File]::OpenWrite($archivePath)
  try { $stream.SetLength($originalLength) } finally { $stream.Dispose() }
  Invoke-TestTool (@('verify') + $common)
  $env:PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM = 'test-only-secret-marker'
  $badOutput = Join-Path $fixtureRoot 'bad-key'
  $badCommon = @('--output', $badOutput, '--version', $identity.Version, '--file-version', $identity.FileVersion, '--commit', $commit, '--policy', $policyPath)
  Invoke-TestTool (@('pack', '--exe', $PublishedExecutable, '--key-id', 'ephemeral-test-key') + $badCommon) -ExpectFailure
  Assert-UpdateCheck (-not (Test-Path $badOutput)) 'invalid secret produced no assets'
  $env:PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM = $key.ExportPkcs8PrivateKeyPem()
  Invoke-TestTool (@('pack', '--exe', $PublishedExecutable, '--key-id', 'ephemeral-test-key', '--output', $badOutput,
    '--version', $identity.Version, '--file-version', $identity.FileVersion, '--commit', ('0' * 40), '--policy', $policyPath)) -ExpectFailure
  Assert-UpdateCheck (-not (Test-Path $badOutput)) 'wrong executable source produced no assets'
  Invoke-TestTool (@('pack', '--exe', $PublishedExecutable, '--key-id', 'ephemeral-test-key', '--output', $badOutput,
    '--version', $identity.Version, '--file-version', $identity.FileVersion, '--commit', $commit,
    '--policy', $unconfiguredPolicyPath)) -ExpectFailure
  Assert-UpdateCheck (-not (Test-Path $badOutput)) 'unconfigured production policy fails closed'
  Assert-UpdateCheck (@(Get-ChildItem -LiteralPath $output -Filter '.verify-*').Count -eq 0) 'verification scratch cleanup'
  Write-Host "Passed $script:updateChecks signed package smoke checks on the real EXE; ephemeral test key, no publication or installed-app changes."
  # Negative cases intentionally leave native exit code 1. Clear it ONLY after
  # all assertions pass, so GitHub's pwsh wrapper doesn't misreport a failed suite.
  $global:LASTEXITCODE = 0
}
finally {
  $env:PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM = $previousSecret
  $key.Dispose()
  Pop-Location
  $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
  $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
  if ($resolvedFixture.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
      (Split-Path -Leaf $resolvedFixture).StartsWith('PrivacyMask-signed-update-test-') -and (Test-Path -LiteralPath $resolvedFixture)) {
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
  }
}
