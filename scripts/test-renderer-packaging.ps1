#requires -Version 7.4
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.RendererTrial.ps1')
$script:checks=0
function Check([bool]$Condition,[string]$Message) {
  if (-not $Condition) { throw $Message }; $script:checks++
}
function Reject([scriptblock]$Action,[string]$Message) {
  $rejected=$false; try { & $Action | Out-Null } catch { $rejected=$true }
  Check $rejected $Message
}
function Status([string]$Scenario='OneWindow') {
  $histogram=[pscustomobject]@{Count=100;MeanMilliseconds=1;MaximumMilliseconds=2;P95UpperBoundMilliseconds=2;P95Overflow=$false}
  [pscustomobject]@{
    State='STATUS';EvidenceKind='LiveRendererWithOwnedSyntheticTargets';Scenario=$Scenario;ProductVersion='fixture'
    Aborted=$false;FullProductionShellStarted=$false;WindowCount=1;FrameCounts=@(101);PixelVariation=@($true)
    MaskStyle='Blur';Strength=0.7;LastFrameAgeSeconds=@(0.01)
    ResponseMetrics=[pscustomobject]@{Active=$true;DurationSeconds=4.1;UpdateCalls=$histogram;TickIntervals=$histogram;MeasuredFrames=@(101);FrameIntervals=@($histogram)}
  }
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('PrivacyMask-renderer-pair-test-'+[guid]::NewGuid().ToString('N'))
try {
  Check ((@(Get-RendererTrialOrder 3 | ForEach-Object { "$($_.Pair):$($_.Variant)" }) -join ',') -ceq
    '1:Compressed,1:Uncompressed,2:Uncompressed,2:Compressed,3:Compressed,3:Uncompressed') 'Unbalanced order.'
  Reject { Get-RendererTrialOrder 0 } 'Zero pairs accepted.'
  $good=Status
  Assert-RendererTrialStatus $good OneWindow fixture STATUS
  Assert-RendererTrialResponse $good OneWindow 4
  Check $true 'Good response rejected.'
  foreach ($property in @('Aborted','FullProductionShellStarted')) {
    $bad=Status; $bad.$property=$true
    Reject { Assert-RendererTrialStatus $bad OneWindow fixture STATUS } "Accepted $property."
  }
  $bad=Status; $bad.PixelVariation=@($false)
  Reject { Assert-RendererTrialStatus $bad OneWindow fixture STATUS } 'Fallback-only capture accepted.'
  $bad=Status; $bad.ResponseMetrics.Active=$false
  Reject { Assert-RendererTrialResponse $bad OneWindow 4 } 'Warmup counted as measurement.'
  $bad=Status; $bad.ResponseMetrics.DurationSeconds=3.9
  Reject { Assert-RendererTrialResponse $bad OneWindow 4 } 'Short response accepted.'
  $bad=Status; $bad.ResponseMetrics.DurationSeconds=10
  Reject { Assert-RendererTrialResponse $bad OneWindow 4 } 'Response interval skew accepted.'
  $bad=Status; $bad.LastFrameAgeSeconds=@(10)
  Reject { Assert-RendererTrialResponse $bad OneWindow 4 } 'Stale frames accepted.'
  $bad=Status; $bad.ResponseMetrics.MeasuredFrames=@(0)
  Reject { Assert-RendererTrialResponse $bad OneWindow 4 } 'No measured frames accepted.'
  $bad=Status; $bad.ResponseMetrics.UpdateCalls.MeanMilliseconds=[double]::NaN
  Reject { Assert-RendererTrialResponse $bad OneWindow 4 } 'NaN timing accepted.'
  $idle=Status IdleFixture; $idle.FrameCounts=@(0); $idle.ResponseMetrics.MeasuredFrames=@(0)
  $idle.ResponseMetrics.FrameIntervals=@([pscustomobject]@{Count=0;MeanMilliseconds=$null;MaximumMilliseconds=$null;P95UpperBoundMilliseconds=$null;P95Overflow=$false})
  Assert-RendererTrialResponse $idle IdleFixture 4
  $idle.FrameCounts=@(1)
  Reject { Assert-RendererTrialResponse $idle IdleFixture 4 } 'Capturing idle fixture accepted.'
  Reject { & (Join-Path $PSScriptRoot 'measure-renderer-packaging.ps1') -OutputDirectory $root } 'Visible opt-in missing.'
  Check (-not (Test-Path -LiteralPath $root)) 'Admission failure wrote evidence.'

  # Harmless local shell copy is only hashed, never executed as a renderer.
  $variants=@(foreach ($name in @('Compressed','Uncompressed')) {
    $directory=Join-Path $root $name
    $null=New-Item -ItemType Directory -Path $directory
    $exe=Join-Path $directory 'PrivacyMask.Rendering.Benchmark.exe'
    Copy-Item -LiteralPath (Get-Process -Id $PID).Path -Destination $exe
    [pscustomobject]@{Name=$name;Compression=($name -ceq 'Compressed');RuntimeConfiguration=@{runtimeOptions=@{includedFrameworks=@(@{name='fixture';version='1'})}}
      Files=@([pscustomobject]@{RelativePath='PrivacyMask.Rendering.Benchmark.exe';Bytes=(Get-Item -LiteralPath $exe).Length;Sha256=(Get-FileHash -LiteralPath $exe).Hash})}
  })
  $assemblies=@{}; foreach ($name in @('PrivacyMask.App','PrivacyMask.Core','PrivacyMask.Windows','PrivacyMask.Updates','PrivacyMask.Rendering.Benchmark')) { $assemblies[$name]='a'*64 }
  $manifest=[pscustomobject]@{SchemaVersion=1;EvidenceKind='MatchedRendererBundles';SourceCommit='fixture';WorkingTreeDirty=$false;DotnetSdk='fixture'
    CommonAssemblyHashes=$assemblies;SingleFile=$true;SelfContained=$true;ReadyToRun=$false;Trimmed=$false;Variants=$variants
    ProductVersion=(Get-Item -LiteralPath (Get-Process -Id $PID).Path).VersionInfo.ProductVersion}
  $manifestPath=Join-Path $root 'prepared.json'
  $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath
  $null=Read-RendererTrialBundles $root fixture fixture
  Check $true 'Valid read-only packet rejected.'
  Reject { Read-RendererTrialBundles $root other fixture } 'Mismatched source accepted.'
  Reject { Read-RendererTrialBundles $root fixture other } 'Mismatched SDK accepted.'
  $manifest.Variants[1].RuntimeConfiguration.runtimeOptions.includedFrameworks[0].version='2'
  $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath
  Reject { Read-RendererTrialBundles $root fixture fixture } 'Mismatched runtime accepted.'
  $manifest.Variants[1].RuntimeConfiguration.runtimeOptions.includedFrameworks[0].version='1'
  $manifest.Variants[0].Files[0].RelativePath='../escaped.exe'
  $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath
  Reject { Read-RendererTrialBundles $root fixture fixture } 'Path traversal accepted.'
  $manifest.Variants[0].Files[0].RelativePath='PrivacyMask.Rendering.Benchmark.exe'
  $manifest.Variants[0].Files[0].Sha256='0'*64
  $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath
  Reject { Read-RendererTrialBundles $root fixture fixture } 'Changed payload accepted.'
  Write-Host "Passed $script:checks renderer-pair checks without opening windows or changing installation."
  $global:LASTEXITCODE=0
} finally {
  $resolved=[IO.Path]::GetFullPath($root)
  if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or
      -not [IO.Path]::GetFileName($resolved).StartsWith('PrivacyMask-renderer-pair-test-',[StringComparison]::Ordinal)) { throw 'Unsafe test cleanup.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
