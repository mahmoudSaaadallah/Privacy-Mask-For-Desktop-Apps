#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot 'artifacts/update-transaction'
Push-Location $repoRoot
try {
  $commit = git rev-parse HEAD
  if ($LASTEXITCODE -ne 0) { throw 'Transaction fixtures need a Git identity.' }
  foreach ($version in @('0.0.0', '0.0.1')) {
    $output = Join-Path $outputRoot $version
    dotnet publish tests/PrivacyMask.Transaction.Peer/PrivacyMask.Transaction.Peer.csproj -c Release -r win-x64 --self-contained true `
      "-p:Version=$version" "-p:InformationalVersion=$version" "-p:FileVersion=$version.0" `
      "-p:SourceRevisionId=$commit" '-p:DebugType=None' '-p:DebugSymbols=false' '-p:TreatWarningsAsErrors=true' -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw "Transaction fixture publish failed: $version" }
  }
  $old = Join-Path $outputRoot '0.0.0/PrivacyMask.Transaction.Peer.exe'
  $target = Join-Path $outputRoot '0.0.1/PrivacyMask.Transaction.Peer.exe'
  dotnet run --project tests/PrivacyMask.Transaction.SmokeChecks -c Release -- $old $target
  if ($LASTEXITCODE -ne 0) { throw 'Isolated update transaction matrix failed.' }
}
finally { Pop-Location }
