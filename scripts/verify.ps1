$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

dotnet test PrivacyMask.Desktop.slnx
dotnet build PrivacyMask.Desktop.slnx
