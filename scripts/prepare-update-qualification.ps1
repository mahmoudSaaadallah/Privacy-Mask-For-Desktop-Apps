param([string]$Name = [Guid]::NewGuid().ToString('N'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Qualification.ps1')
Push-Location $repoRoot
try {
  $dirty = git status --porcelain --untracked-files=normal
  if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit reviewed source before preparing a provenance-labelled packet.' }
  $commit = git rev-parse --verify HEAD
  if ($LASTEXITCODE -ne 0) { throw 'Missing committed source identity.' }
  $output = New-PrivacyMaskQualificationPacket -RepoRoot $repoRoot -SourceCommit $commit -Name $Name
  Write-Host "Prepared a NotRun lab packet: $output"
  Write-Host 'No VM provisioned, key generated, App executed, installation changed or release authorized.'
}
finally { Pop-Location }
