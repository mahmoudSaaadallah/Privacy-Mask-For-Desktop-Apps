#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-renderer-gate-' + [Guid]::NewGuid().ToString('N'))
try {
  $output = Join-Path $root 'bin'
  dotnet build (Join-Path $repo 'tests/PrivacyMask.Rendering.Benchmark') -c Release --artifacts-path (Join-Path $root 'build') -o $output --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Renderer fixture build failed.' }
  $exe = Join-Path $output 'PrivacyMask.Rendering.Benchmark.exe'
  foreach ($arguments in @(
      @('--scenario','OneWindow','--duration-seconds','5'),
      @('--scenario','Unknown','--duration-seconds','5','--allow-visible-fixtures','true'),
      @('--scenario','OneWindow','--duration-seconds','1','--allow-visible-fixtures','true'))) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 2) { throw 'Unsafe/invalid live fixture arguments were accepted.' }
  }
  $rejected = $false
  try { & (Join-Path $PSScriptRoot 'measure-renderer-baseline.ps1') -OutputDirectory (Join-Path $root 'not-admitted') }
  catch { $rejected = $true }
  if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $root 'not-admitted'))) { throw 'Visible session ran without explicit admission.' }
  $global:LASTEXITCODE = 0
  Write-Host 'Renderer argument gates passed without opening windows or capturing content.'
}
finally {
  $resolved = [IO.Path]::GetFullPath($root)
  if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or
      -not ([IO.Path]::GetFileName($resolved)).StartsWith('PrivacyMask-renderer-gate-', [StringComparison]::Ordinal)) { throw 'Unsafe renderer gate cleanup.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
