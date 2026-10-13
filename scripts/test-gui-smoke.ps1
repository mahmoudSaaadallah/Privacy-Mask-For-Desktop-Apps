param(
  [string]$ScreenshotDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests/PrivacyMask.Gui.SmokeChecks/PrivacyMask.Gui.SmokeChecks.csproj'
if ([string]::IsNullOrWhiteSpace($ScreenshotDirectory)) {
  dotnet run --project $project -c Release
}
else {
  $output = [System.IO.Path]::GetFullPath($ScreenshotDirectory)
  dotnet run --project $project -c Release -- $output
}
if ($LASTEXITCODE -ne 0) {
  throw "Compiled WPF GUI smoke checks failed with exit code $LASTEXITCODE."
}
