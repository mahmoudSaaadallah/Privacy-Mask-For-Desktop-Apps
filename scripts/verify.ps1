$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

& .\scripts\verify-powershell-syntax.ps1
dotnet test PrivacyMask.Desktop.slnx
dotnet build PrivacyMask.Desktop.slnx
