$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PrivacyMask.Build.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("PrivacyMask-version-test-$([Guid]::NewGuid().ToString('N'))")
$script:policyChecks = 0

function Write-VersionFixture {
  param([string]$Version = '2.0.0-beta.1', [string]$FileVersion = '2.0.0.1',
        [string]$AssemblyVersion = '2.0.0.0', [string]$Info = '$(Version)')
  $fixture = [xml]'<Project><PropertyGroup><Version/><AssemblyVersion/><FileVersion/><InformationalVersion/></PropertyGroup></Project>'
  $fixture.Project.PropertyGroup.Version = $Version
  $fixture.Project.PropertyGroup.AssemblyVersion = $AssemblyVersion
  $fixture.Project.PropertyGroup.FileVersion = $FileVersion
  $fixture.Project.PropertyGroup.InformationalVersion = $Info
  $fixture.Save((Join-Path $fixtureRoot 'Directory.Build.props'))
}

function Assert-Policy {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
  $script:policyChecks++
}
function Assert-Rejected {
  param([scriptblock]$Action, [string]$Label)
  $rejected = $false
  try { & $Action | Out-Null } catch { $rejected = $true }
  Assert-Policy $rejected "Expected rejection: $Label"
}

try {
  New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
  foreach ($version in @('2.0.0', '2.0.0-beta.1', '2.0.0-beta.2', '2.0.0-rc.1', '2.0.0-alpha.0', '2.0.0-beta-a.12')) {
    Write-VersionFixture -Version $version
    $identity = Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot
    Assert-Policy ($identity.Version -ceq $version -and $identity.FileVersion -eq '2.0.0.1') "Wrong identity for $version"
    $flags = @(Get-PrivacyMaskReleaseFlags -RepoRoot $fixtureRoot -Tag "v$version")
    Assert-Policy ($flags -contains '--verify-tag') 'Release command may invent a missing tag.'
    if ($version.Contains('-')) {
      Assert-Policy ($identity.IsPrerelease -and $flags -contains '--prerelease' -and $flags -contains '--latest=false') 'Prerelease could be published as stable/latest.'
    }
    else {
      Assert-Policy (-not $identity.IsPrerelease -and $flags.Count -eq 1) 'Stable version incorrectly classified.'
    }
  }
  foreach ($version in @('02.0.0', '2.00.0', '2.0', '2.0.0-', '2.0.0-beta..1', '2.0.0-beta.01', '2.0.0-beta_1', '2.0.0+manual-build', '2.0.0 beta.1')) {
    Write-VersionFixture -Version $version
    Assert-Rejected { Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot } "invalid product $version"
  }
  foreach ($fileVersion in @('2.0.0', '2.0.0-beta.1', '2.0.1.1', '2.0.0.65535', '2.0.0.-1')) {
    Write-VersionFixture -FileVersion $fileVersion
    Assert-Rejected { Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot } "invalid Windows version $fileVersion"
  }
  Write-VersionFixture -AssemblyVersion '2.1.0.0'
  Assert-Rejected { Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot } 'mismatched assembly version'
  Write-VersionFixture -Info '2.0.0'
  Assert-Rejected { Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot } 'stale informational version'
  Write-VersionFixture
  foreach ($tag in @('v2.0.0', 'v2.0.0-beta.2', 'v2.0.0-BETA.1', '2.0.0-beta.1')) {
    Assert-Rejected { Get-PrivacyMaskReleaseFlags -RepoRoot $fixtureRoot -Tag $tag } "mismatched tag $tag"
  }
  $expected = Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot
  Assert-PrivacyMaskPackageVersion -Identity $expected -FileVersion '2.0.0.1' -ProductVersion '2.0.0-beta.1+abcdef1234'
  Assert-Policy $true 'Matching package metadata failed.'
  foreach ($product in @('2.0.0', '2.0.0-beta.2', '2.0.0-rc.1', '2.0.0-beta.1stale', '2.0.0-BETA.1')) {
    Assert-Rejected { Assert-PrivacyMaskPackageVersion -Identity $expected -FileVersion '2.0.0.1' -ProductVersion $product } "stale/wrong package product $product"
  }
  Assert-Rejected { Assert-PrivacyMaskPackageVersion -Identity $expected -FileVersion '2.0.0.0' -ProductVersion '2.0.0-beta.1' } 'stale package file revision'

  Write-VersionFixture -Version '2.0.1' -AssemblyVersion '2.0.1.0' -FileVersion '2.0.1.7'
  Assert-Policy ((Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot).Version -eq '2.0.1') 'Patch identity failed.'
  Write-VersionFixture -Version '2.1.0-beta.1' -AssemblyVersion '2.1.0.0' -FileVersion '2.1.0.1'
  Assert-Policy ((Get-PrivacyMaskVersionIdentity -RepoRoot $fixtureRoot).Version -eq '2.1.0-beta.1') 'Minor identity failed.'

  # Parse every inline PowerShell block and execute the actual publishing block
  # against an in-scope gh stub. No token, network, tag or release is involved.
  $workflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/release.yml') -Raw
  $current = Get-PrivacyMaskVersionIdentity -RepoRoot $repoRoot
  $blocks = [Regex]::Matches($workflow, '(?m)^        run: \|\r?\n(?<Body>(?:          [^\r\n]*(?:\r?\n|$))+)')
  foreach ($block in $blocks) {
    $body = ($block.Groups['Body'].Value -replace '(?m)^          ', '')
    $body = $body.Replace('${{ github.ref_name }}', $current.ExpectedTag).Replace('${{ github.repository }}', 'fixture/repo')
    $tokens = $null; $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseInput($body, [ref]$tokens, [ref]$parseErrors) | Out-Null
    Assert-Policy ($parseErrors.Count -eq 0) 'Invalid PowerShell inside release workflow.'
  }
  Assert-Policy ($blocks.Count -eq 2) 'Release workflow validation/publishing blocks changed; review this test.'
  $publish = ($blocks[1].Groups['Body'].Value -replace '(?m)^          ', '')
  $publish = $publish.Replace('${{ github.ref_name }}', $current.ExpectedTag).Replace('${{ github.repository }}', 'fixture/repo')
  $captured = New-Object 'System.Collections.Generic.List[string]'
  Push-Location $repoRoot
  try {
    & {
      function gh {
        param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
        foreach ($argument in $Arguments) { $captured.Add($argument) }
        $script:LASTEXITCODE = 0
      }
      & ([scriptblock]::Create($publish))
    }
  }
  finally { Pop-Location }
  Assert-Policy ($captured.Count -gt 3 -and $captured[0] -eq 'release' -and $captured[1] -eq 'create' -and $captured[2] -ceq $current.ExpectedTag) 'Wrong release/tag command.'
  Assert-Policy ($captured -contains '--verify-tag') 'Publishing block omitted tag protection.'
  Assert-Policy (($captured -contains '--prerelease') -eq $current.IsPrerelease -and ($captured -contains '--latest=false') -eq $current.IsPrerelease) 'Publishing block misclassified prerelease.'
  Write-Host "Version policy passed $script:policyChecks checks, including mocked workflow publication (no external writes)."
}
finally {
  $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
  $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
  if ($resolvedFixture.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
      (Split-Path -Leaf $resolvedFixture).StartsWith('PrivacyMask-version-test-') -and (Test-Path -LiteralPath $resolvedFixture)) {
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
  }
}
