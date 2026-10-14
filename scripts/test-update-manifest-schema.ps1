#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestSchema = Join-Path $repoRoot 'docs/contracts/update-manifest.schema.json'
$signatureSchema = Join-Path $repoRoot 'docs/contracts/update-signature.schema.json'
$script:contractChecks = 0

# Synthetic structure only: these hashes/signatures identify no release or EXE.
# No keys, network, disk fixtures, installation or cryptographic verification.
$script:baselineJson = @'
{
  "schemaVersion": 1,
  "appId": "PrivacyMask.Desktop",
  "repository": "mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps",
  "version": "2.0.0-beta.3",
  "tag": "v2.0.0-beta.3",
  "commitSha": "1111111111111111111111111111111111111111",
  "channel": "preview",
  "runtime": "win-x64",
  "minWindowsBuild": 19041,
  "minUpdaterProtocol": 1,
  "package": {
    "assetName": "PrivacyMask-2.0.0-beta.3-win-x64-update.zip",
    "size": 1024,
    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  },
  "files": [{
    "path": "PrivacyMask.App.exe",
    "size": 2048,
    "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
  }],
  "settingsCompatibility": {
    "minimumReadableSchema": 1,
    "maximumReadableSchema": 12,
    "outputSchema": 12
  }
}
'@

function Assert-SchemaResult {
  param([string]$Json, [string]$Schema, [bool]$Expected, [string]$Label)
  $actual = Test-Json -Json $Json -SchemaFile $Schema -ErrorAction SilentlyContinue
  if ([bool]$actual -ne $Expected) { throw "Unexpected schema result: $Label" }
  $script:contractChecks++
}

function Assert-ManifestMutation {
  param([scriptblock]$Change, [string]$Label, [bool]$Expected = $false)
  $candidate = $script:baselineJson | ConvertFrom-Json
  & $Change $candidate
  Assert-SchemaResult -Json ($candidate | ConvertTo-Json -Depth 12 -Compress) `
    -Schema $manifestSchema -Expected $Expected -Label $Label
}

Assert-SchemaResult $script:baselineJson $manifestSchema $true 'synthetic preview manifest'
Assert-SchemaResult '{' $manifestSchema $false 'malformed JSON'
Assert-SchemaResult '[]' $manifestSchema $false 'non-object manifest'

foreach ($version in @('0.0.0', '2.0.0', '2.1.0', '2.0.0-beta.10', '2.0.0-rc.1', '2.0.0-alpha-a.0', '2.0.0-0')) {
  Assert-ManifestMutation {
    param($m)
    $m.version = $version
    $m.tag = "v$version"
    $m.package.assetName = "PrivacyMask-$version-win-x64-update.zip"
    $m.channel = if ($version.Contains('-')) { 'preview' } else { 'stable' }
  } "valid product structure: $version" $true
}
foreach ($version in @('02.0.0', '2.00.0', '2.0', '2.0.0-', '2.0.0-beta.01', '2.0.0-beta..1', '2.0.0-beta_1', '2.0.0+build', '2.0.0 beta.1', "2.0.0-beta.3`n")) {
  Assert-ManifestMutation { param($m) $m.version = $version } 'invalid product version'
}

Assert-ManifestMutation { param($m) $m.schemaVersion = 2 } 'unknown schema'
Assert-ManifestMutation { param($m) $m.PSObject.Properties.Remove('appId') } 'missing identity'
Assert-ManifestMutation { param($m) $m.appId = 'AnotherApp' } 'wrong identity'
Assert-ManifestMutation { param($m) $m.repository = 'someone/else' } 'wrong repo'
Assert-ManifestMutation { param($m) $m.runtime = 'win-arm64' } 'wrong runtime'
Assert-ManifestMutation { param($m) $m.channel = 'stable' } 'preview labelled stable'
Assert-ManifestMutation { param($m) $m.version = '2.0.0' } 'stable labelled preview'
Assert-ManifestMutation { param($m) $m | Add-Member NoteProperty command 'run-this' } 'unknown root field'
Assert-ManifestMutation { param($m) $m.tag += "`n" } 'newline tag'
Assert-ManifestMutation { param($m) $m.commitSha += "`n" } 'newline commit'
Assert-ManifestMutation { param($m) $m.commitSha = 'not-a-commit' } 'invalid commit'
Assert-ManifestMutation { param($m) $m.minWindowsBuild = 0 } 'invalid OS minimum'
Assert-ManifestMutation { param($m) $m.minUpdaterProtocol = 0 } 'invalid protocol'
Assert-ManifestMutation { param($m) $m.minUpdaterProtocol = '1' } 'coerced protocol'

foreach ($size in @(0, -1, 1.5, 536870913, '1024')) {
  Assert-ManifestMutation { param($m) $m.package.size = $size } 'invalid archive size'
  Assert-ManifestMutation { param($m) $m.files[0].size = $size } 'invalid payload size'
}
Assert-ManifestMutation { param($m) $m.package.size = 536870912; $m.files[0].size = 536870912 } 'inclusive size ceiling' $true
Assert-ManifestMutation { param($m) $m.package.assetName = '../update.zip' } 'unsafe asset name'
Assert-ManifestMutation { param($m) $m.package.assetName += "`n" } 'newline asset'
Assert-ManifestMutation { param($m) $m.package.sha256 = 'A' * 64 } 'noncanonical hash'
Assert-ManifestMutation { param($m) $m.package.sha256 = 'a' * 63 } 'short hash'
Assert-ManifestMutation { param($m) $m.files[0].sha256 += "`n" } 'newline file hash'
Assert-ManifestMutation { param($m) $m.package | Add-Member NoteProperty url 'https://example.invalid' } 'untrusted URL field'
Assert-ManifestMutation { param($m) $m.files = @() } 'missing payload'
Assert-ManifestMutation { param($m) $m.files += $m.files[0] } 'extra or duplicate file'
foreach ($path in @('../PrivacyMask.App.exe', 'C:\PrivacyMask.App.exe', 'PrivacyMask.App.exe:stream', 'privacymask.app.exe', 'settings.v1.json', 'Install-PrivacyMask.ps1', 'PrivacyMask.Launcher.exe')) {
  Assert-ManifestMutation { param($m) $m.files[0].path = $path } 'non-allowlisted payload'
}
Assert-ManifestMutation { param($m) $m.files[0] | Add-Member NoteProperty linkTarget 'outside' } 'link metadata'
Assert-ManifestMutation { param($m) $m.settingsCompatibility.outputSchema = 0 } 'invalid output schema'
Assert-ManifestMutation { param($m) $m.settingsCompatibility | Add-Member NoteProperty pin '1234' } 'unexpected credential field'

# These deliberately pass STRUCTURE, proving why the runtime must check semantics.
Assert-ManifestMutation { param($m) $m.tag = 'v9.0.0' } 'tag/version binding needs semantic verifier' $true
Assert-ManifestMutation { param($m) $m.package.assetName = 'PrivacyMask-9.0.0-win-x64-update.zip' } 'asset/version binding needs semantic verifier' $true
Assert-ManifestMutation { param($m) $m.settingsCompatibility.minimumReadableSchema = 20 } 'schema interval needs semantic verifier' $true

$envelope = [ordered]@{
  algorithm = 'ECDSA-P256-SHA256'
  keyId = 'synthetic-test-key'
  signature = [Convert]::ToBase64String([byte[]]::new(64))
}
$envelopeJson = $envelope | ConvertTo-Json -Compress
Assert-SchemaResult $envelopeJson $signatureSchema $true '64 zero bytes: structure only, not valid ECDSA'
foreach ($mutation in @(
  { param($s) $s.algorithm = 'none' },
  { param($s) $s.keyId = '' },
  { param($s) $s.keyId += "`n" },
  { param($s) $s.signature = [Convert]::ToBase64String([byte[]]::new(63)) },
  { param($s) $s.signature = [Convert]::ToBase64String([byte[]]::new(65)) },
  { param($s) $s.signature = '!' * 88 },
  { param($s) $s.signature = ('A' * 85) + 'B==' },
  { param($s) $s.signature = $s.signature.TrimEnd('=') },
  { param($s) $s | Add-Member NoteProperty publicKey 'attacker-key' }
)) {
  $candidate = $envelopeJson | ConvertFrom-Json
  & $mutation $candidate
  Assert-SchemaResult ($candidate | ConvertTo-Json -Compress) $signatureSchema $false 'invalid signature envelope structure'
}

Write-Host "Passed $script:contractChecks update schema checks. Structural validation only; no cryptographic or runtime recovery claim."
