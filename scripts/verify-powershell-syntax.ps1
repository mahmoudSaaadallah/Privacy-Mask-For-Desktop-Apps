$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$searchRoots = @(
  (Join-Path $repoRoot 'scripts')
  (Join-Path $repoRoot 'desktop-app\windows\win-x64')
)

$scriptFiles = @(
  $searchRoots |
    Where-Object { Test-Path -LiteralPath $_ } |
    ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File -Filter '*.ps1' }
)

$failures = New-Object System.Collections.Generic.List[object]
foreach ($scriptFile in $scriptFiles) {
  $tokens = $null
  $parseErrors = $null
  [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptFile.FullName,
    [ref]$tokens,
    [ref]$parseErrors) | Out-Null

  foreach ($parseError in $parseErrors) {
    $failures.Add([PSCustomObject]@{
      File = $scriptFile.FullName.Substring($repoRoot.Length + 1)
      Line = $parseError.Extent.StartLineNumber
      Column = $parseError.Extent.StartColumnNumber
      Message = $parseError.Message
    })
  }
}

if ($failures.Count -gt 0) {
  $failures | Format-Table File, Line, Column, Message -AutoSize
  throw "PowerShell syntax validation failed with $($failures.Count) error(s)."
}

Write-Host "Validated PowerShell syntax for $($scriptFiles.Count) script(s)."
