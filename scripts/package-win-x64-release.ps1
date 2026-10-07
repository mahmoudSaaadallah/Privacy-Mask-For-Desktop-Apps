param(
  [string]$OutputDirectory = 'artifacts/release',
  [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
  [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
  [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}

$distributionRoot = Join-Path $repoRoot 'desktop-app\windows\win-x64'
$publishedExecutable = Join-Path $distributionRoot 'single-file\PrivacyMask.App.exe'
$stagingPath = Join-Path $outputPath 'PrivacyMask-win-x64'
$archivePath = Join-Path $outputPath 'PrivacyMask-win-x64.zip'
$checksumPath = "$archivePath.sha256"

& (Join-Path $PSScriptRoot 'publish-win-x64-single-file.ps1') -SkipTests:$SkipTests

if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
  throw "Published executable not found at '$publishedExecutable'."
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

if (Test-Path -LiteralPath $stagingPath) {
  Remove-Item -LiteralPath $stagingPath -Recurse -Force
}

New-Item -ItemType Directory -Path (Join-Path $stagingPath 'single-file') -Force | Out-Null

$packageFiles = @(
  'Install-PrivacyMask.cmd'
  'Install-PrivacyMask.ps1'
  'Uninstall-PrivacyMask.cmd'
  'Uninstall-PrivacyMask.ps1'
  'README.md'
)

foreach ($packageFile in $packageFiles) {
  Copy-Item `
    -LiteralPath (Join-Path $distributionRoot $packageFile) `
    -Destination (Join-Path $stagingPath $packageFile)
}

Copy-Item `
  -LiteralPath $publishedExecutable `
  -Destination (Join-Path $stagingPath 'single-file\PrivacyMask.App.exe')

Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stagingPath '*') -DestinationPath $archivePath -CompressionLevel Optimal

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$archiveHash  PrivacyMask-win-x64.zip" -Encoding ascii

Remove-Item -LiteralPath $stagingPath -Recurse -Force

Write-Host "Created release package: $archivePath"
Write-Host "Created SHA-256 checksum: $checksumPath"
