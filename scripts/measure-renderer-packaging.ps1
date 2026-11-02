#requires -Version 7.4
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [string]$PreparedDirectory='',
  [switch]$PrepareOnly,
  [switch]$AllowVisibleFixtures,
  [ValidateSet('IdleFixture','OneWindow','TwoWindows','MoveOnly','MoveResize','RevealInputs')]
  [string[]]$Scenarios=@('OneWindow','MoveResize','RevealInputs'),
  [ValidateRange(0,600)][int]$WarmupSeconds=30,
  [ValidateRange(2,600)][int]$DurationSeconds=60,
  [ValidateRange(1,5)][int]$Pairs=3
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.RendererTrial.ps1')
if (-not $PrepareOnly -and -not $AllowVisibleFixtures) { throw 'Explicit -AllowVisibleFixtures required for owned visible targets.' }
if ($PrepareOnly -and $PreparedDirectory) { throw 'Preparation cannot reuse a bundle.' }
if (-not [OperatingSystem]::IsWindows() -or -not [Environment]::Is64BitProcess) { throw 'Requires Windows x64 PowerShell.' }
if ($Scenarios.Count -eq 0 -or @($Scenarios|Select-Object -Unique).Count -ne $Scenarios.Count) { throw 'Choose unique scenarios.' }
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
$commit=git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Missing source identity.' }
if (-not [string]::IsNullOrWhiteSpace((git -C $repo status --porcelain | Out-String))) { throw 'Commit reviewed tools before preparing/measuring matched bundles.' }
$sdk=dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Missing SDK.' }
$version=([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$null=New-Item -ItemType Directory -Path $output
$bundleRoot=if ($PreparedDirectory) { [IO.Path]::GetFullPath($PreparedDirectory) } else { $output }
if (-not $PreparedDirectory) {
  $variants=[Collections.Generic.List[object]]::new()
  $common=@{}
  foreach ($name in @('Compressed','Uncompressed')) {
    $compressed=$name -ceq 'Compressed'
    $flag=if ($compressed) { 'true' } else { 'false' }
    $bin=Join-Path $output $name
    $build=Join-Path $output "build-$name"
    dotnet publish (Join-Path $repo 'tests/PrivacyMask.Rendering.Benchmark') -c Release -r win-x64 --self-contained true `
      --artifacts-path $build -o $bin --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
      "-p:EnableCompressionInSingleFile=$flag" -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:DebugSymbols=false -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "Renderer $name publish failed." }
    foreach ($assembly in @('PrivacyMask.Rendering.Benchmark','PrivacyMask.App','PrivacyMask.Core','PrivacyMask.Windows','PrivacyMask.Updates')) {
      $files=@(Get-ChildItem -LiteralPath (Join-Path $build 'bin') -Recurse -File -Filter "$assembly.dll")
      $hashes=@($files | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Sort-Object -Unique)
      if ($hashes.Count -ne 1) { throw 'Missing/ambiguous compiled assembly.' }
      if ($compressed) { $common[$assembly]=$hashes[0] }
      elseif ($common[$assembly] -cne $hashes[0]) { throw 'Managed fixture/product assemblies differ; reject comparison.' }
    }
    $configs=@(Get-ChildItem -LiteralPath $build -Recurse -File -Filter 'PrivacyMask.Rendering.Benchmark.runtimeconfig.json')
    if ($configs.Count -ne 1) { throw 'Ambiguous renderer runtime configuration.' }
    $exe=Get-Item -LiteralPath (Join-Path $bin 'PrivacyMask.Rendering.Benchmark.exe')
    if ($exe.VersionInfo.ProductVersion -cne "$version+$commit") { throw 'Unexpected fixture version.' }
    $files=@(Get-ChildItem -LiteralPath $bin -Recurse -File | ForEach-Object {
      [pscustomobject]@{RelativePath=[IO.Path]::GetRelativePath($bin,$_.FullName);Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $variants.Add([pscustomobject]@{Name=$name;Compression=$compressed;Files=$files;RuntimeConfiguration=(Get-Content -LiteralPath $configs[0].FullName -Raw|ConvertFrom-Json)})
  }
  [pscustomobject]@{
    SchemaVersion=1;EvidenceKind='MatchedRendererBundles';SourceCommit=$commit;WorkingTreeDirty=$false;DotnetSdk=$sdk
    ProductVersion="$version+$commit";CommonAssemblyHashes=$common;Variants=$variants.ToArray()
    SingleFile=$true;SelfContained=$true;ReadyToRun=$false;Trimmed=$false
  } | ConvertTo-Json -Depth 12 | Out-File -LiteralPath (Join-Path $output 'prepared.json') -NoClobber -Encoding utf8
}
$prepared=Read-RendererTrialBundles $bundleRoot $commit $sdk
if ($PrepareOnly) { Write-Host "Prepared matched renderer bundles only: $output"; return }
function Assert-NoOtherApp {
  if (@(Get-Process -Name PrivacyMask.App,PrivacyMask.Rendering.Benchmark -ErrorAction SilentlyContinue).Count) {
    throw 'Exit the App/other renderer normally before comparison; no process will be stopped for you.'
  }
}
function Read-Response([Diagnostics.Process]$Process) {
  $line=$Process.StandardOutput.ReadLineAsync()
  if (-not $line.Wait(30000) -or [string]::IsNullOrWhiteSpace($line.Result)) { throw 'Renderer did not respond.' }
  return ($line.Result | ConvertFrom-Json)
}
$results=[Collections.Generic.List[object]]::new()
$order=@(Get-RendererTrialOrder $Pairs)
foreach ($scenario in $Scenarios) {
  foreach ($step in $order) {
    Assert-NoOtherApp
    $null=Read-RendererTrialBundles $bundleRoot $commit $sdk
    $name=$step.Variant
    $prefix="$scenario-$($step.Pair)-$name"
    $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $bundleRoot "$name/PrivacyMask.Rendering.Benchmark.exe"))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path $output "cache-$name"
    foreach ($argument in @('--scenario',$scenario,'--duration-seconds',"$($WarmupSeconds+$DurationSeconds+90)",'--allow-visible-fixtures','true')) { $info.ArgumentList.Add($argument) }
    $process=[Diagnostics.Process]::Start($info)
    $record=$null
    try {
      $stderr=$process.StandardError.ReadToEndAsync()
      $ready=Read-Response $process
      Assert-RendererTrialStatus $ready $scenario $prepared.ProductVersion 'READY'
      $ready | ConvertTo-Json -Depth 12 | Out-File -LiteralPath (Join-Path $output "$prefix-admission.json") -NoClobber
      Write-Host "$prefix : warmup $WarmupSeconds s, sample $DurationSeconds s; owned synthetic windows only."
      if ($WarmupSeconds) { Start-Sleep -Seconds $WarmupSeconds }
      $process.StandardInput.WriteLine('begin-measurement')
      $start=Read-Response $process
      Assert-RendererTrialStatus $start $scenario $prepared.ProductVersion 'MEASURING'
      if (-not $start.ResponseMetrics.Active -or $start.ResponseMetrics.UpdateCalls.Count -ne 0) { throw 'Warmup metrics were not reset.' }
      $path=Join-Path $output "$prefix-runtime.json"
      & (Join-Path $PSScriptRoot 'measure-runtime.ps1') -TargetProcessId $process.Id -ExpectedStartTimeUtc $process.StartTime.ToUniversalTime() `
        -DurationSeconds $DurationSeconds -SampleIntervalMilliseconds 500 -RequireFullDuration -OutputPath $path | Out-Null
      $process.StandardInput.WriteLine('status')
      $status=Read-Response $process
      $status | ConvertTo-Json -Depth 12 | Out-File -LiteralPath (Join-Path $output "$prefix-status.json") -NoClobber
      Assert-RendererTrialStatus $status $scenario $prepared.ProductVersion 'STATUS'
      Assert-RendererTrialResponse $status $scenario $DurationSeconds
      $measurement=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
      if ($measurement.ProductVersion -cne $prepared.ProductVersion) { throw 'Measured version changed.' }
      $record=[pscustomobject]@{Scenario=$scenario;Pair=$step.Pair;Variant=$name;Measurement=$measurement;Response=$status.ResponseMetrics;Capture=$status}
    }
    finally {
      if (-not $process.HasExited) {
        try { $process.StandardInput.WriteLine('stop'); $process.StandardInput.Close() } catch [IO.IOException] { }
        if (-not $process.WaitForExit(20000)) { $process.Kill(); $process.WaitForExit() }
      }
      $exitCode=$process.ExitCode
      $stderr.GetAwaiter().GetResult() | Out-File -LiteralPath (Join-Path $output "$prefix-stderr.txt") -NoClobber
      $process.Dispose()
    }
    if ($exitCode -ne 0) { throw 'Owned synthetic renderer exited abnormally; reject pass.' }
    Assert-NoOtherApp
    $record | ConvertTo-Json -Depth 14 | Out-File -LiteralPath (Join-Path $output "$prefix-result.json") -NoClobber
    $results.Add($record)
  }
}
[pscustomobject]@{
  EvidenceKind='PairedSyntheticRendererPackaging';Qualification='NotEvaluated';Prepared=$prepared
  WarmupSeconds=$WarmupSeconds;DurationSeconds=$DurationSeconds;Order=$order;Scenarios=$Scenarios;Passes=$results.ToArray()
  Scope='Production renderer plus owned synthetic targets; excludes full App, updater, real input/display latency, startup latency and Stable qualification.'
} | ConvertTo-Json -Depth 18 | Out-File -LiteralPath (Join-Path $output 'summary.json') -NoClobber
Write-Host "Completed synthetic renderer comparison. No installation or production setting changed. Evidence: $output"
