#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot 'artifacts/update-transaction/framework'
$buildRoot = Join-Path $outputRoot 'build'
Push-Location $repoRoot
try {
  $commit = git rev-parse HEAD
  if ($LASTEXITCODE -ne 0) { throw 'Transaction fixtures need a Git identity.' }
  foreach ($version in @('0.0.0', '0.0.1')) {
    $output = Join-Path $outputRoot $version
    # Test peers reuse the developer/CI .NET runtime: no need to copy another runtime
    # through every fault case. This does NOT change the self-contained App release.
    dotnet publish tests/PrivacyMask.Transaction.Peer/PrivacyMask.Transaction.Peer.csproj -c Release -r win-x64 --self-contained false `
      '-p:SelfContained=false' '-p:PublishSelfContained=false' '-p:EnableCompressionInSingleFile=false' `
      "-p:Version=$version" "-p:InformationalVersion=$version" "-p:FileVersion=$version.0" `
      "-p:SourceRevisionId=$commit" '-p:DebugType=None' '-p:DebugSymbols=false' '-p:TreatWarningsAsErrors=true' `
      --artifacts-path $buildRoot -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw "Transaction fixture publish failed: $version" }
    $peer = Get-Item -LiteralPath (Join-Path $output 'PrivacyMask.Transaction.Peer.exe')
    if ($peer.Length -gt 2MB) { throw 'Transaction peer unexpectedly includes a runtime or exceeded its reviewed fixture budget.' }
    Write-Host "Transaction peer $version`: $($peer.Length) bytes (developer framework-dependent fixture)."
  }
  $old = Join-Path $outputRoot '0.0.0/PrivacyMask.Transaction.Peer.exe'
  $target = Join-Path $outputRoot '0.0.1/PrivacyMask.Transaction.Peer.exe'
  $hostOutput = Join-Path $outputRoot 'host'
  dotnet publish tests/PrivacyMask.Transaction.SmokeChecks -c Release -r win-x64 --self-contained false `
    '-p:DebugType=None' '-p:DebugSymbols=false' '-p:TreatWarningsAsErrors=true' --artifacts-path $buildRoot -o $hostOutput --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Transaction smoke host publish failed.' }
  dotnet (Join-Path $hostOutput 'PrivacyMask.Transaction.SmokeChecks.dll') $old $target
  if ($LASTEXITCODE -ne 0) { throw 'Isolated update transaction matrix failed.' }
}
finally { Pop-Location }
