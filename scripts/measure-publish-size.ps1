[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [switch]$NoRestore,

  [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-PathMeasurement {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$DistributionType
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    return [PSCustomObject]@{
      Name = $Name
      DistributionType = $DistributionType
      Exists = $false
      FileCount = 0
      SizeBytes = 0
      SizeMb = 0
    }
  }

  $item = Get-Item -LiteralPath $Path
  $files = @(
    if ($item -is [System.IO.DirectoryInfo]) {
      Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Force
    }
    else {
      $item
    }
  )

  $sizeBytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
  return [PSCustomObject]@{
    Name = $Name
    DistributionType = $DistributionType
    Exists = $true
    FileCount = $files.Count
    SizeBytes = $sizeBytes
    SizeMb = [Math]::Round($sizeBytes / 1MB, 3)
  }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\PrivacyMask.App\PrivacyMask.App.csproj'
$portablePath = Join-Path $repoRoot 'desktop-app\windows\win-x64\app'
$singleFilePath = Join-Path $repoRoot 'desktop-app\windows\win-x64\single-file\PrivacyMask.App.exe'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PrivacyMask-publish-size-' + [Guid]::NewGuid().ToString('N'))
$frameworkDependentPath = Join-Path $temporaryRoot 'framework-dependent'

New-Item -ItemType Directory -Path $frameworkDependentPath -Force | Out-Null

try {
  $publishArguments = @(
    'publish'
    $projectPath
    '--configuration'
    $Configuration
    "--property:PublishDir=$frameworkDependentPath"
    '--property:SelfContained=false'
    '--property:PublishSingleFile=false'
    '--property:DebugSymbols=false'
    '--property:DebugType=None'
    '--property:SatelliteResourceLanguages=en'
  )

  if ($NoRestore) {
    $publishArguments += '--no-restore'
  }

  & dotnet @publishArguments
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
  }

  $measurements = @(
    Get-PathMeasurement -Name 'Current portable build' -Path $portablePath -DistributionType 'Self-contained folder'
    Get-PathMeasurement -Name 'Current single-file build' -Path $singleFilePath -DistributionType 'Self-contained single file'
    Get-PathMeasurement -Name 'Measured framework-dependent build' -Path $frameworkDependentPath -DistributionType 'Framework-dependent folder'
  )

  $result = [PSCustomObject]@{
    CapturedAtUtc = [DateTime]::UtcNow.ToString('o')
    Configuration = $Configuration
    Measurements = $measurements
  }

  $measurements | Format-Table Name, DistributionType, Exists, FileCount, SizeMb -AutoSize

  if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $resolvedOutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
      New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }

    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
    Write-Host "Saved publish-size measurements to $resolvedOutputPath"
  }
}
finally {
  $systemTemporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
  $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
  if (-not $resolvedTemporaryRoot.StartsWith($systemTemporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove non-temporary measurement directory '$resolvedTemporaryRoot'."
  }

  if (Test-Path -LiteralPath $resolvedTemporaryRoot) {
    Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
  }
}
