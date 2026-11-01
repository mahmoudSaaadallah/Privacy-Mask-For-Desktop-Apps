$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop-app/windows/win-x64/ShortcutTools.ps1')
$script:checks = 0
function Assert-Startup([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }; $script:checks++
}
$legacy = 'C:\Users\Example User\PrivacyMask.Desktop\PrivacyMask.App.exe'
$app = 'C:\Users\Example User\PrivacyMask.Desktop\app\PrivacyMask.App.exe'
$launcher = 'C:\Users\Example User\PrivacyMask.Desktop\PrivacyMask.Launcher.exe'
$paths = @($legacy, $app, $launcher)
foreach ($path in $paths) {
  foreach ($minimized in @($false, $true)) {
    $command = '"' + $path + '"'
    $expected = '"' + $launcher + '"'
    if ($minimized) { $command += ' --minimized'; $expected += ' --minimized' }
    $match = Get-PrivacyMaskStartupMatch -Value $command -ExecutablePaths $paths
    Assert-Startup ($match.Matches -and $match.StartMinimized -eq $minimized) 'Exact known command did not match.'
    $next = Get-PrivacyMaskStartupMigration -Value $command -ExecutablePaths $paths -LauncherPath $launcher
    if ($path -ceq $launcher) { Assert-Startup ($null -eq $next) 'Unchanged launcher command would be rewritten.' }
    else { Assert-Startup ($next -ceq $expected) 'Legacy migration did not preserve minimized preference.' }
  }
}
foreach ($bad in @($null, 42, '', ('cmd.exe /c "' + $launcher + '"'),
    ('"' + $launcher + '" --minimized --unknown'), ('"' + $launcher + '.other.exe"'),
    ('"' + $app + '" --other'), ('powershell.exe -File "' + $legacy + '"'))) {
  Assert-Startup (-not (Get-PrivacyMaskStartupMatch -Value $bad -ExecutablePaths $paths).Matches) 'Foreign or unsupported command matched our startup entry.'
  Assert-Startup ($null -eq (Get-PrivacyMaskStartupMigration -Value $bad -ExecutablePaths $paths -LauncherPath $launcher)) 'Foreign or missing command would be rewritten.'
}
$caseCommand = '  "' + $launcher.ToUpperInvariant() + '" --MINIMIZED  '
$caseMatch = Get-PrivacyMaskStartupMatch -Value $caseCommand -ExecutablePaths $paths
Assert-Startup ($caseMatch.Matches -and $caseMatch.StartMinimized) 'Case/outer whitespace not recognized.'
Write-Host "Passed $script:checks startup-command checks; pure values only, no registry access, installation or process launch."
