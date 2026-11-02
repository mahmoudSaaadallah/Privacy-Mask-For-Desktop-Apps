Set-StrictMode -Version Latest

function Get-RendererTrialOrder([int]$Pairs) {
  if ($Pairs -lt 1 -or $Pairs -gt 5) { throw 'Choose one to five pairs.' }
  for ($pair=1; $pair -le $Pairs; $pair++) {
    $names=if ($pair % 2) { @('Compressed','Uncompressed') } else { @('Uncompressed','Compressed') }
    foreach ($name in $names) { [pscustomobject]@{Pair=$pair;Variant=$name} }
  }
}

function Assert-RendererTrialStatus($Status,[string]$Scenario,[string]$ProductVersion,[string]$State) {
  $windows=if ($Scenario -ceq 'TwoWindows') { 2 } else { 1 }
  if ($Status.State -cne $State -or $Status.EvidenceKind -cne 'LiveRendererWithOwnedSyntheticTargets' -or
      $Status.Scenario -cne $Scenario -or $Status.ProductVersion -cne $ProductVersion -or
      $Status.Aborted -or $Status.FullProductionShellStarted -or $Status.WindowCount -ne $windows -or
      $Status.FrameCounts.Count -ne $windows -or $Status.PixelVariation.Count -ne $windows -or
      $Status.MaskStyle -cne 'Blur' -or $Status.Strength -ne 0.7) { throw 'Unexpected renderer identity/state.' }
  if ($Scenario -cne 'IdleFixture' -and @($Status.PixelVariation | Where-Object { -not $_ }).Count) {
    throw 'Missing varied synthetic captures.'
  }
}

function Assert-RendererTrialResponse($Status,[string]$Scenario,[int]$DurationSeconds) {
  $response=$Status.ResponseMetrics
  if (-not $response.Active -or -not [double]::IsFinite($response.DurationSeconds) -or
      $response.DurationSeconds -lt $DurationSeconds -or $response.DurationSeconds -gt $DurationSeconds+5 -or
      $response.UpdateCalls.Count -lt $DurationSeconds -or $response.TickIntervals.Count -lt $DurationSeconds -or
      $response.MeasuredFrames.Count -ne $Status.WindowCount -or $response.FrameIntervals.Count -ne $Status.WindowCount) {
    throw 'Incomplete renderer response interval.'
  }
  foreach ($histogram in @($response.UpdateCalls,$response.TickIntervals)+@($response.FrameIntervals)) {
    if ($histogram.Count -gt 0 -and ($null -eq $histogram.MeanMilliseconds -or
        -not [double]::IsFinite($histogram.MeanMilliseconds) -or $histogram.MeanMilliseconds -lt 0 -or
        -not [double]::IsFinite($histogram.MaximumMilliseconds) -or $histogram.MaximumMilliseconds -lt $histogram.MeanMilliseconds -or
        (-not $histogram.P95Overflow -and ($null -eq $histogram.P95UpperBoundMilliseconds -or
          $histogram.P95UpperBoundMilliseconds -lt 0)))) { throw 'Invalid renderer timing metrics.' }
  }
  for ($index=0; $index -lt $Status.WindowCount; $index++) {
    $frames=$response.MeasuredFrames[$index]
    if ($Scenario -ceq 'IdleFixture') {
      if ($frames -ne 0 -or $Status.FrameCounts[$index] -ne 0) { throw 'Idle fixture captured content.' }
    } elseif ($frames -lt [math]::Max(2,$DurationSeconds/4) -or
        $response.FrameIntervals[$index].Count -ne $frames-1 -or $Status.LastFrameAgeSeconds[$index] -gt 5) {
      throw 'Stale/insufficient captures; timing alone is not a live-renderer pass.'
    }
  }
}

function Read-RendererTrialBundles([string]$Directory,[string]$Commit,[string]$Sdk) {
  $manifest=Get-Content -LiteralPath (Join-Path $Directory 'prepared.json') -Raw | ConvertFrom-Json
  if ($manifest.SchemaVersion -ne 1 -or $manifest.EvidenceKind -cne 'MatchedRendererBundles' -or
      $manifest.SourceCommit -cne $Commit -or $manifest.DotnetSdk -cne $Sdk -or $manifest.WorkingTreeDirty -or
      $manifest.Variants.Count -ne 2 -or (@($manifest.Variants.Name|Sort-Object) -join ',') -cne 'Compressed,Uncompressed' -or
      (@($manifest.CommonAssemblyHashes.PSObject.Properties.Name|Sort-Object) -join ',') -cne
        'PrivacyMask.App,PrivacyMask.Core,PrivacyMask.Rendering.Benchmark,PrivacyMask.Updates,PrivacyMask.Windows' -or
      -not $manifest.SingleFile -or -not $manifest.SelfContained -or $manifest.ReadyToRun -or $manifest.Trimmed) { throw 'Prepared renderer/source identity mismatch.' }
  $runtimes=@($manifest.Variants | ForEach-Object { $_.RuntimeConfiguration | ConvertTo-Json -Depth 12 -Compress } | Sort-Object -Unique)
  if ($runtimes.Count -ne 1) { throw 'Runtime configurations differ.' }
  foreach ($variant in $manifest.Variants) {
    $root=[IO.Path]::GetFullPath((Join-Path $Directory $variant.Name))
    if ($variant.Files.Count -lt 1 -or @(Get-ChildItem -LiteralPath $root -Recurse -File).Count -ne $variant.Files.Count -or
        $variant.Compression -ne ($variant.Name -ceq 'Compressed')) { throw 'Unexpected renderer payload.' }
    foreach ($file in $variant.Files) {
      $path=[IO.Path]::GetFullPath((Join-Path $root $file.RelativePath))
      if ([IO.Path]::IsPathFullyQualified($file.RelativePath) -or
          -not $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
          (Get-Item -LiteralPath $path).Length -ne $file.Bytes -or
          (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.Sha256) { throw 'Renderer payload changed.' }
    }
    $exe=Get-Item -LiteralPath (Join-Path $root 'PrivacyMask.Rendering.Benchmark.exe')
    if ($exe.VersionInfo.ProductVersion -cne $manifest.ProductVersion) { throw 'Renderer executable version changed.' }
  }
  return $manifest
}
