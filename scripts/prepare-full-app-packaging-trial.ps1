#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
. (Join-Path $PSScriptRoot 'PrivacyMask.PackagingTrial.ps1')
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh trial directory.' }
$identity=Get-PrivacyMaskVersionIdentity -RepoRoot $repo
if (-not $identity.IsPrerelease) { throw 'Full-App packaging trials are Preview-only.' }
$commit=(git -C $repo rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Missing source identity.' }
$dirty=-not [string]::IsNullOrWhiteSpace((git -C $repo status --porcelain|Out-String))
$sdk=(dotnet --version)
if ($LASTEXITCODE -ne 0) { throw 'Missing SDK.' }
$profile=[xml](Get-Content -LiteralPath (Join-Path $repo 'src/PrivacyMask.App/Properties/PublishProfiles/WinX64SingleFile.pubxml') -Raw)
if ($profile.Project.PropertyGroup.EnableCompressionInSingleFile -cne 'true') { throw 'This C/U/C trial expects the current compressed default.' }
$null=New-Item -ItemType Directory -Path $output
$launcherOutput=Join-Path $output 'launcher'
dotnet publish (Join-Path $repo 'src/PrivacyMask.Launcher/PrivacyMask.Launcher.csproj') -c Release -r win-x64 --self-contained true `
  --artifacts-path (Join-Path $output 'build-launcher') -o $launcherOutput --nologo -p:PublishAot=true -p:DebugType=None -p:DebugSymbols=false -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { throw 'Trial launcher publish failed; no fallback or installation.' }
$launcher=Join-Path $launcherOutput 'PrivacyMask.Launcher.exe'
$variants=[Collections.Generic.List[object]]::new()
$commonHashes=@{}
foreach ($name in @('Compressed','Uncompressed')) {
  $compressed=$name -ceq 'Compressed'
  $flag=if ($compressed) { 'true' } else { 'false' }
  $root=Join-Path $output $name
  $bin=Join-Path $root 'publish'
  $build=Join-Path $output "build-$name"
  dotnet publish (Join-Path $repo 'src/PrivacyMask.App/PrivacyMask.App.csproj') -c Release -r win-x64 --self-contained true `
    --artifacts-path $build -o $bin --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    "-p:EnableCompressionInSingleFile=$flag" -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
  if ($LASTEXITCODE -ne 0) { throw "Full App $name publish failed." }
  $app=Get-Item -LiteralPath (Join-Path $bin 'PrivacyMask.App.exe')
  Assert-PrivacyMaskPackageVersion -Identity $identity -FileVersion $app.VersionInfo.FileVersion -ProductVersion $app.VersionInfo.ProductVersion
  if ($app.VersionInfo.ProductVersion -cne "$($identity.Version)+$commit") { throw 'Unexpected full source version.' }
  foreach ($assembly in @('PrivacyMask.App','PrivacyMask.Core','PrivacyMask.Windows','PrivacyMask.Updates')) {
    $files=@(Get-ChildItem -LiteralPath (Join-Path $build 'bin') -Recurse -File -Filter "$assembly.dll")
    if ($files.Count -eq 0) { throw 'Missing compiled product assembly.' }
    $hashes=@($files|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}|Sort-Object -Unique)
    if ($hashes.Count -ne 1) { throw 'Ambiguous product assembly identity.' }
    if ($compressed) { $commonHashes[$assembly]=$hashes[0] }
    elseif ($commonHashes[$assembly] -cne $hashes[0]) { throw 'Managed product assemblies differ between packaging variants.' }
  }
  $configs=@(Get-ChildItem -LiteralPath $build -Recurse -File -Filter 'PrivacyMask.App.runtimeconfig.json')
  if ($configs.Count -ne 1) { throw 'Ambiguous full-App runtime configuration.' }
  $runtime=Get-Content -LiteralPath $configs[0].FullName -Raw|ConvertFrom-Json
  $package=Join-Path $root 'package'
  $single=Join-Path $package 'single-file'
  $null=New-Item -ItemType Directory -Path $single
  foreach ($file in @('Install-PrivacyMask.cmd','Install-PrivacyMask.ps1','ShortcutTools.ps1','Uninstall-PrivacyMask.cmd','Uninstall-PrivacyMask.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repo "desktop-app/windows/win-x64/$file") -Destination (Join-Path $package $file)
  }
  $readme=@"
# PrivacyMask full-App packaging trial: $name

Diagnostic build of $($identity.Version), source $commit. Not a published release.
This pair changes bundle compression only. FileVersion: $($identity.FileVersion).
Switch through the reviewed checkout's invoke-full-app-packaging-step.ps1 after
exiting PrivacyMask yourself. Stage C1 uses Compressed, U1 uses Uncompressed,
and C2 restores Compressed. The helper requires explicit -RunForCurrentUser.
Do not publish either same-version diagnostic payload as a new update release.
Both variants retain the normal required updater and protection defaults.
The uncompressed EXE is larger; native extraction, staging and rollback storage
are additional. Successful install is not performance or Stable qualification.
"@
  $readme|Set-Content -LiteralPath (Join-Path $package 'README.md') -Encoding utf8
  Copy-Item -LiteralPath $app.FullName -Destination (Join-Path $single 'PrivacyMask.App.exe')
  Copy-Item -LiteralPath $launcher -Destination (Join-Path $single 'PrivacyMask.Launcher.exe')
  $files=@(Get-ChildItem -LiteralPath $package -Recurse -File|ForEach-Object{
    [pscustomobject]@{RelativePath=[IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/');Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
  })
  $archive=Join-Path $root "PrivacyMask-$($identity.Version)-win-x64.zip"
  Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive -CompressionLevel Optimal
  $archiveHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
  "$archiveHash  $([IO.Path]::GetFileName($archive))"|Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
  $variants.Add([pscustomobject]@{
    Name=$name;Compression=$compressed;AppSha256=(Get-FileHash -LiteralPath $app.FullName -Algorithm SHA256).Hash
    ExeBytes=$app.Length;PackageBytes=($files.Bytes|Measure-Object -Sum).Sum;ZipBytes=(Get-Item -LiteralPath $archive).Length
    ArchiveSha256=$archiveHash;RuntimeConfiguration=$runtime;Files=$files
  })
}
[pscustomobject]@{
  SchemaVersion=1;EvidenceKind='FullAppPackagingTrial';TrialId=[guid]::NewGuid().ToString('N');ManualOnly=$true
  ProductVersion=$identity.Version;FullProductVersion="$($identity.Version)+$commit";FileVersion=$identity.FileVersion
  SourceCommit=$commit;WorkingTreeDirty=$dirty;DotnetSdk=$sdk;CreatedAtUtc=[datetime]::UtcNow.ToString('o')
  LauncherSha256=(Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash;CommonProductAssemblyHashes=$commonHashes
  ProductionDefaultsChanged=$false;AppExecutedByPreparation=$false;Qualification='NotEvaluated';Variants=$variants.ToArray()
}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $output 'trial.json') -Encoding utf8
$trial=Read-PackagingTrial $output
$trial.Variants|Select-Object Name,ExeBytes,PackageBytes,ZipBytes|Format-Table
Write-Host "Prepared manual full-App C/U/C trial only. No installation or App execution performed. Packet: $output"
