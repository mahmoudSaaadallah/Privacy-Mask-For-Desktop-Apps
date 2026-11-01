#requires -Version 7.0
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [string]$PreparedDirectory='',
  [switch]$PrepareOnly,
  [ValidateRange(0,600)][int]$WarmupSeconds=30,
  [ValidateRange(2,600)][int]$DurationSeconds=60,
  [ValidateRange(1,5)][int]$Passes=3
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if (-not [Environment]::Is64BitProcess -or -not [OperatingSystem]::IsWindows()) { throw 'Requires Windows and 64-bit PowerShell 7.' }
if ($PrepareOnly -and $PreparedDirectory) { throw 'PrepareOnly cannot reuse prepared bundles.' }
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
$commit=(git -C $repo rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Missing source commit.' }
$dirty=-not [string]::IsNullOrWhiteSpace((git -C $repo status --porcelain | Out-String))
$sdk=(dotnet --version)
if ($LASTEXITCODE -ne 0) { throw 'Missing SDK.' }
$version=([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$null=New-Item -ItemType Directory -Path $output
$bundleRoot=if ($PreparedDirectory) { [IO.Path]::GetFullPath($PreparedDirectory) } else { $output }

if (-not $PreparedDirectory) {
  $variants=[Collections.Generic.List[object]]::new()
  foreach($name in @('Compressed','Uncompressed')) {
    $compressed=$name -ceq 'Compressed'
    $bin=Join-Path $output $name
    $build=Join-Path $output "build-$name"
    $flag=if ($compressed) {'true'} else {'false'}
    dotnet publish (Join-Path $repo 'tests/PrivacyMask.Gui.Benchmark/PrivacyMask.Gui.Benchmark.csproj') -c Release -r win-x64 --self-contained true `
      --artifacts-path $build -o $bin --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
      "-p:EnableCompressionInSingleFile=$flag" -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:DebugSymbols=false -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "Synthetic $name publish failed." }
    $exe=Get-Item -LiteralPath (Join-Path $bin 'PrivacyMask.Gui.Benchmark.exe')
    $files=@(Get-ChildItem -LiteralPath $bin -Recurse -File | ForEach-Object {
      [pscustomobject]@{RelativePath=[IO.Path]::GetRelativePath($bin,$_.FullName);Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $archive=Join-Path $output "$name.zip"
    Compress-Archive -Path (Join-Path $bin '*') -DestinationPath $archive -CompressionLevel Optimal
    $configs=@(Get-ChildItem -LiteralPath $build -Recurse -File -Filter 'PrivacyMask.Gui.Benchmark.runtimeconfig.json')
    if ($configs.Count -ne 1) { throw 'Ambiguous fixture runtime metadata.' }
    $runtime=Get-Content -LiteralPath $configs[0].FullName -Raw | ConvertFrom-Json
    $variants.Add([pscustomobject]@{
      Name=$name; Compression=$compressed; ProductVersion=$exe.VersionInfo.ProductVersion; Files=$files
      ExeBytes=$exe.Length; PayloadBytes=($files.Bytes|Measure-Object -Sum).Sum; ZipBytes=(Get-Item -LiteralPath $archive).Length
      RuntimeConfiguration=$runtime
    })
  }
  [pscustomobject]@{
    SchemaVersion=1; EvidenceKind='SyntheticCompressionBundles'; SourceCommit=$commit; WorkingTreeDirty=$dirty; DotnetSdk=$sdk
    ProductVersion=$version; SingleFile=$true; SelfContained=$true; ReadyToRun=$false; Trimmed=$false; Variants=$variants.ToArray()
  }|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $output 'prepared.json') -Encoding utf8
}
$prepared=Get-Content -LiteralPath (Join-Path $bundleRoot 'prepared.json') -Raw | ConvertFrom-Json
if ($prepared.SchemaVersion -ne 1 -or $prepared.EvidenceKind -cne 'SyntheticCompressionBundles' -or
    $prepared.SourceCommit -cne $commit -or $prepared.ProductVersion -cne $version -or $prepared.DotnetSdk -cne $sdk -or
    -not $prepared.SingleFile -or -not $prepared.SelfContained -or $prepared.ReadyToRun -or $prepared.Trimmed -or
    $prepared.Variants.Count -ne 2 -or (@($prepared.Variants.Name | Sort-Object) -join ',') -cne 'Compressed,Uncompressed') {
  throw 'Prepared experiment/source/build identity mismatch.'
}
$runtimeA=$prepared.Variants[0].RuntimeConfiguration|ConvertTo-Json -Depth 10 -Compress
$runtimeB=$prepared.Variants[1].RuntimeConfiguration|ConvertTo-Json -Depth 10 -Compress
if ($runtimeA -cne $runtimeB) { throw 'Runtime configurations differ; compression is not the only variable.' }
function Assert-Variant($Variant) {
  $root=[IO.Path]::GetFullPath((Join-Path $bundleRoot $Variant.Name))
  if (@(Get-ChildItem -LiteralPath $root -Recurse -File).Count -ne $Variant.Files.Count) { throw 'Prepared payload file count changed.' }
  foreach($file in $Variant.Files) {
    $path=[IO.Path]::GetFullPath((Join-Path $root $file.RelativePath))
    if ([IO.Path]::IsPathFullyQualified($file.RelativePath) -or -not $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        (Get-Item -LiteralPath $path).Length -ne $file.Bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.Sha256) { throw 'Prepared payload identity changed.' }
  }
}
foreach($variant in $prepared.Variants) {
  if ($variant.Compression -ne ($variant.Name -ceq 'Compressed') -or $variant.ProductVersion -cne "$version+$commit") { throw 'Prepared version/compression metadata mismatch.' }
  Assert-Variant $variant
}
if ($PrepareOnly) { Write-Host "Prepared two synthetic bundles only; no processes measured. Evidence: $output"; return }

if (-not ('PrivacyMask.Diagnostics.RegionQuery' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'PrivacyMask.MemoryRegions.cs') }
$reports=[Collections.Generic.List[object]]::new()
$lifetime=$WarmupSeconds+$DurationSeconds+60
for($pass=1;$pass -le $Passes;$pass++) {
  $order=if ($pass%2 -eq 1) { @('Compressed','Uncompressed') } else { @('Uncompressed','Compressed') }
  foreach($name in $order) {
    $variant=$prepared.Variants | Where-Object Name -CEQ $name
    Assert-Variant $variant
    $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $bundleRoot "$name/PrivacyMask.Gui.Benchmark.exe"))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $cache=Join-Path $output "cache-$name"
    $info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=$cache
    foreach($argument in @('--scenario','Overview','--duration-seconds',"$lifetime",'--control-stdin','true')) { $info.ArgumentList.Add($argument) }
    $readyTimer=[Diagnostics.Stopwatch]::StartNew()
    $process=[Diagnostics.Process]::Start($info)
    try {
      $stderr=$process.StandardError.ReadToEndAsync()
      $ready=$process.StandardOutput.ReadLineAsync()
      if (-not $ready.Wait(30000) -or $process.HasExited -or [string]::IsNullOrWhiteSpace($ready.Result)) { throw 'Owned synthetic bundle did not become ready.' }
      $identity=$ready.Result|ConvertFrom-Json
      $readyMilliseconds=$readyTimer.Elapsed.TotalMilliseconds
      if ($identity.State -cne 'READY' -or $identity.EvidenceKind -cne 'SyntheticHiddenGui' -or $identity.ProfileCount -ne 42 -or
          $identity.Scenario -cne 'Overview' -or $identity.ProductVersion -cne $variant.ProductVersion -or
          $identity.ProductionRuntimeStarted -ne $false -or $identity.CaptureStarted -ne $false -or
          $identity.Distribution -cne 'SelfContainedSingleFile' -or -not $identity.BenchmarkBuild.SingleFile -or
          -not $identity.BenchmarkBuild.SelfContained -or $identity.BenchmarkBuild.Compression -ne $variant.Compression) { throw 'Unexpected synthetic bundle readiness/build identity.' }
      $identity|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output "$name-$pass-admission.json") -Encoding utf8
      Write-Host "Synthetic $name pass $pass/${Passes}: warm-up $WarmupSeconds s, sample $DurationSeconds s. No full App/capture/startup/settings/network."
      $path=Join-Path $output "$name-$pass-runtime.json"
      & (Join-Path $PSScriptRoot 'measure-runtime.ps1') -TargetProcessId $process.Id -ExpectedStartTimeUtc $process.StartTime.ToUniversalTime() `
        -WarmupSeconds $WarmupSeconds -DurationSeconds $DurationSeconds -SampleIntervalMilliseconds 500 -RequireFullDuration -OutputPath $path|Out-Null
      $measurement=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
      if ($measurement.ProductVersion -cne $identity.ProductVersion) { throw 'Measured bundle identity mismatch.' }
      $regions=[PrivacyMask.Diagnostics.RegionQuery]::Capture($process.Id)
      $report=[pscustomobject]@{Variant=$name;Pass=$pass;Identity=$identity;Measurement=$measurement;Regions=$regions;ReadyMilliseconds=$readyMilliseconds}
      $report|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $output "$name-$pass.json") -Encoding utf8
      $reports.Add($report)
    }
    finally {
      if (-not $process.HasExited) {
        try { $process.StandardInput.WriteLine('stop'); $process.StandardInput.Close() } catch [IO.IOException] { }
        if (-not $process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit() }
      }
      $exitCode=$process.ExitCode
      $process.Dispose()
    }
    if ($exitCode -ne 0) { throw 'Owned synthetic bundle did not exit normally.' }
    $stderr.GetAwaiter().GetResult()|Set-Content -LiteralPath (Join-Path $output "$name-$pass-stderr.txt") -Encoding utf8
  }
}
$means=@(foreach($variant in $prepared.Variants) {
  $rows=@($reports|Where-Object Variant -CEQ $variant.Name)
  $privateExecutable=@($rows|ForEach-Object {
    $category=$_.Regions.Categories|Where-Object Kind -CEQ 'Private executable'
    if ($null -eq $category) { 0d } else { $category.CommittedVirtualMiB }
  })
  $cache=Join-Path $output "cache-$($variant.Name)"
  $cacheBytes=if (Test-Path -LiteralPath $cache) { (Get-ChildItem -LiteralPath $cache -Recurse -File|Measure-Object Length -Sum).Sum } else { 0 }
  [pscustomobject]@{
    Variant=$variant.Name; Passes=$rows.Count
    WorkingSetMiB=[math]::Round(($rows.Measurement.WorkingSetMb.Average|Measure-Object -Average).Average,3)
    PrivateMiB=[math]::Round(($rows.Measurement.PrivateMemoryMb.Average|Measure-Object -Average).Average,3)
    MachineCpuPercent=[math]::Round(($rows.Measurement.MachineCpuPercent.Average|Measure-Object -Average).Average,4)
    Handles=[math]::Round(($rows.Measurement.HandleCount.Average|Measure-Object -Average).Average,2)
    PrivateExecutableCommittedMiB=[math]::Round(($privateExecutable|Measure-Object -Average).Average,3)
    ExeBytes=$variant.ExeBytes; PayloadBytes=$variant.PayloadBytes; ZipBytes=$variant.ZipBytes; BundleNativeCacheBytes=$cacheBytes
  }
})
[pscustomobject]@{
  EvidenceKind='SyntheticHiddenGuiCompressionComparison'; Qualification='NotEvaluated'; SourceCommit=$commit; WorkingTreeDirty=$dirty
  DotnetSdk=$sdk; PreparedBundles=$prepared; WarmupSeconds=$WarmupSeconds; PassDurationSeconds=$DurationSeconds; SampleIntervalMilliseconds=500
  Scope='Owned hidden synthetic GUI only. Not full App/tray/live blur/install or Stable qualification. Ready time is not controlled cold-start latency.'
  Order=@($reports|ForEach-Object { "$($_.Variant)-$($_.Pass)" }); Means=$means; Passes=$reports.ToArray()
}|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
$means|Format-Table Variant,WorkingSetMiB,PrivateMiB,MachineCpuPercent,PrivateExecutableCommittedMiB,ExeBytes,ZipBytes
Write-Host "Synthetic compression experiment completed. Full-App choice still requires matched full-App measurements and disk/cache trade-off review. Evidence: $output"
