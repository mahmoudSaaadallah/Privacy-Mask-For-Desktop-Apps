Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')

function New-PrivacyMaskQualificationPacket {
  param([Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$SourceCommit,
    [Parameter(Mandatory = $true)][string]$Name)
  if ($SourceCommit -cnotmatch '^[0-9a-f]{40}$' -or $Name -cnotmatch '^[a-z0-9][a-z0-9-]{0,47}$') {
    throw 'Expected a source commit and a short lower-case packet name, never a path.'
  }
  $identity = Get-PrivacyMaskVersionIdentity -RepoRoot $RepoRoot
  $approvalPath = Join-Path $RepoRoot 'build/update-signing-approval.json'
  if ((Get-Item -LiteralPath $approvalPath).Length -gt 4096) { throw 'Invalid review policy size.' }
  $approval = Get-Content -LiteralPath $approvalPath -Raw | ConvertFrom-Json
  if ($approval.schemaVersion -ne 1 -or $approval.reviewPolicy -cnotin @('Unapproved', 'SoloMaintainer', 'IndependentReview')) {
    throw 'Invalid planned review policy.'
  }
  $templatePath = Join-Path $RepoRoot 'docs/templates/update-qualification-plan.json'
  $runbookPath = Join-Path $RepoRoot 'docs/update-qualification-runbook.md'
  foreach ($source in @($templatePath, $runbookPath)) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -gt 64KB) {
      throw 'Missing or oversized qualification template.'
    }
  }
  $plan = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
  if ($plan.schemaVersion -ne 1 -or @($plan.operatingSystems).Count -ne 2 -or
      $plan.operatingSystems[0] -cne 'windows-10-x64' -or $plan.operatingSystems[1] -cne 'windows-11-x64' -or
      @($plan.cases).Count -lt 20 -or @($plan.cases).Count -gt 64) { throw 'Invalid qualification plan.' }
  $ids = @{}
  foreach ($case in $plan.cases) {
    if ($case.id -cnotmatch '^[a-z0-9-]{1,64}$' -or $ids.ContainsKey($case.id) -or
        $case.expected -isnot [string] -or $case.expected.Length -gt 1024 -or $case.expected.Length -eq 0) {
      throw 'Invalid or duplicate qualification case.'
    }
    $ids[$case.id] = $true
  }
  $environments = @($plan.operatingSystems | ForEach-Object {
    [ordered]@{ id = $_; osBuild = $null; standardUser = $null; snapshot = $null;
      baselineVersion = $null; baselineCommit = $null; baselineArchiveSha256 = $null;
      targetVersion = $null; targetCommit = $null; targetArchiveSha256 = $null;
      installedLauncherVersion = $null; installedLauncherSha256 = $null;
      approvedPublicKeyFingerprint = $null;
      results = @($plan.cases | ForEach-Object { [ordered]@{ id = $_.id; expected = $_.expected; status = 'NotRun'; evidence = $null } }) }
  })
  $report = [ordered]@{
    schemaVersion = 1; purpose = 'Preparation only; not qualification or release authority'; overallStatus = 'NotRun';
    sourceCommit = $SourceCommit; sourceVersion = $identity.Version; sourceFileVersion = $identity.FileVersion;
    plannedReviewPolicy = $approval.reviewPolicy;
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('o');
    approvals = @{ keyCustody = 'Pending'; publicTrust = 'Pending'; bootstrap = 'Pending'; publication = 'Pending' };
    environments = $environments
  }
  # Fixed workspace-only descendants. No overwrites or recursive cleanup; preserve partial output on failure.
  $base = [IO.Path]::GetFullPath($RepoRoot)
  foreach ($part in @('artifacts', 'update-qualification')) {
    $base = Join-Path $base $part
    if (Test-Path -LiteralPath $base) {
      $item = Get-Item -LiteralPath $base -Force
      if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Qualification output must use ordinary workspace directories.'
      }
    }
    else { New-Item -ItemType Directory -Path $base | Out-Null }
  }
  $output = Join-Path $base ('packet-' + $Name)
  if (Test-Path -LiteralPath $output) { throw 'Packet already exists. Choose a fresh name; no files were overwritten.' }
  New-Item -ItemType Directory -Path $output | Out-Null
  $encoding = New-Object Text.UTF8Encoding($false)
  $files = @{
    'evidence.json' = $encoding.GetBytes(($report | ConvertTo-Json -Depth 12))
    'RUNBOOK.md' = [IO.File]::ReadAllBytes($runbookPath)
  }
  foreach ($entry in $files.GetEnumerator()) {
    $stream = [IO.File]::Open((Join-Path $output $entry.Key), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($entry.Value, 0, $entry.Value.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
  }
  return $output
}
