# Owner guide regression only: all GitHub, OpenSSL and interactive inputs are
# intercepted. No real encrypted file, key, network call or secret upload is used.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$guide = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/update-signing-secret-setup.md') -Raw
$match = [regex]::Match($guide, '(?s)```powershell\s*\r?\n(.*?)\r?\n```')
if (-not $match.Success) { throw 'Owner guide command block missing.' }
$parseTokens = $null
$parseErrors = $null
$null = [Management.Automation.Language.Parser]::ParseInput($match.Groups[1].Value, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Owner guide command block has syntax errors.' }
$guideAction = [scriptblock]::Create($match.Groups[1].Value)
$global:privacyMaskGuideFixture = @{
    Checks = 0; Case = ''; EnvironmentReads = 0; UploadAttempts = 0
    Pem = "-----BEGIN PRIVATE KEY-----`nVEVTVA==`n-----END PRIVATE KEY-----"
    Public = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAET51tmvseX6BRpk7j0M+xCnvYyMogUFwUBHYBGaw6MZeoA26Dm/GgGX/bGHr9PVej4XPOR4QTUnoiKlALmTnZ4A=='
}

function Assert-SecretGuide {
    param([bool]$Condition, [string]$Label)
    if (-not $Condition) { throw "Secret guide fixture failed: $Label" }
    $global:privacyMaskGuideFixture.Checks++
}

function Get-Command {
    param([string]$Name, [string]$CommandType, [string]$ErrorAction)
    if ($Name -cne 'openssl' -or $CommandType -cne 'Application') { throw 'Unexpected command lookup.' }
    [pscustomobject]@{ Source = 'Invoke-FixtureOpenSsl' }
}
function Test-Path {
    param([string]$LiteralPath, [string]$PathType)
    if ($PathType -cne 'Leaf') { throw 'Unexpected file lookup.' }
    if ($LiteralPath -ceq 'Invoke-FixtureOpenSsl') { return $global:privacyMaskGuideFixture.Case -cne 'missing-openssl' }
    if ($LiteralPath -ceq 'encrypted-fixture.pem') { return $global:privacyMaskGuideFixture.Case -cne 'missing-file' }
    throw 'Unexpected file lookup.'
}
function Read-Host {
    param([string]$Prompt)
    if ($Prompt.StartsWith('Full path')) { return 'encrypted-fixture.pem' }
    if ($Prompt.StartsWith('Type UPLOAD')) {
        if ($global:privacyMaskGuideFixture.Case -ceq 'cancel') { return 'CANCEL' }
        return 'UPLOAD'
    }
    throw 'Unexpected prompt.'
}
function Invoke-FixtureOpenSsl {
    $global:LASTEXITCODE = 0
    $arguments = @($args)
    if ($arguments -contains '-in') {
        if ($global:privacyMaskGuideFixture.Case -ceq 'decrypt-failed') { $global:LASTEXITCODE = 1; return }
        if ($global:privacyMaskGuideFixture.Case -ceq 'empty-key') { return }
        if ($global:privacyMaskGuideFixture.Case -ceq 'oversize-key') { return ('x' * 16385) }
        if ($global:privacyMaskGuideFixture.Case -ceq 'encrypted-key') { return '-----BEGIN ENCRYPTED PRIVATE KEY-----' }
        if ($global:privacyMaskGuideFixture.Case -ceq 'multiple-keys') { return ($global:privacyMaskGuideFixture.Pem + "`n" + $global:privacyMaskGuideFixture.Pem) }
        return $global:privacyMaskGuideFixture.Pem
    }
    if ($arguments -contains '-pubout') {
        $received = @($input) -join "`n"
        if ($received -cne $global:privacyMaskGuideFixture.Pem) { throw 'Unexpected OpenSSL stdin.' }
        if ($global:privacyMaskGuideFixture.Case -ceq 'derive-failed') { $global:LASTEXITCODE = 1; return }
        if ($global:privacyMaskGuideFixture.Case -ceq 'wrong-key') { return "-----BEGIN PUBLIC KEY-----`nV1JPTkc=`n-----END PUBLIC KEY-----" }
        return "-----BEGIN PUBLIC KEY-----`n$($global:privacyMaskGuideFixture.Public)`n-----END PUBLIC KEY-----"
    }
    throw 'Unexpected OpenSSL invocation.'
}
function gh {
    $arguments = @($args)
    $global:LASTEXITCODE = 0
    if ($arguments[0] -ceq 'api') {
        if ($arguments[1] -ceq 'user') {
            if ($global:privacyMaskGuideFixture.Case -ceq 'wrong-account') { return 'another-owner' }
            return 'mahmoudSaaadallah'
        }
        $route = @($arguments | Where-Object { $_ -like 'repos/*' })[0]
        if ($route.EndsWith('/privacy-mask-release')) {
            $global:privacyMaskGuideFixture.EnvironmentReads++
            $bypass = $global:privacyMaskGuideFixture.Case -ceq 'unsafe-environment' -or
                ($global:privacyMaskGuideFixture.Case -ceq 'late-unsafe-environment' -and $global:privacyMaskGuideFixture.EnvironmentReads -eq 2)
            return (@{ name = 'privacy-mask-release'; can_admins_bypass = $bypass
                protection_rules = @(@{ type = 'required_reviewers'; prevent_self_review = $false
                    reviewers = @(@{ type = 'User'; reviewer = @{ id = 123; login = 'mahmoudSaaadallah' } }) })
                deployment_branch_policy = @{ custom_branch_policies = $true; protected_branches = $false }
            } | ConvertTo-Json -Depth 10)
        }
        if ($route.EndsWith('/deployment-branch-policies?per_page=100')) {
            return '{"total_count":1,"branch_policies":[{"name":"v*","type":"tag"}]}'
        }
        if ($route.EndsWith('/variables/PRIVACYMASK_UPDATE_KEY_ID')) {
            if ($global:privacyMaskGuideFixture.Case -ceq 'wrong-key-id') { return '{"value":"other-key"}' }
            return '{"value":"release-key-1"}'
        }
        if ($route.EndsWith('/secrets?per_page=100')) {
            if ($global:privacyMaskGuideFixture.Case -ceq 'inventory-unavailable') { $global:LASTEXITCODE = 1; return }
            if ($global:privacyMaskGuideFixture.Case -ceq 'existing-secret') {
                return '{"total_count":1,"secrets":[{"name":"PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM"}]}'
            }
            if ($global:privacyMaskGuideFixture.Case -ceq 'incomplete-inventory') { return '{"total_count":101,"secrets":[]}' }
            return '{"total_count":0,"secrets":[]}'
        }
        throw 'Unexpected GitHub read route.'
    }
    if ($arguments[0] -ceq 'secret' -and $arguments[1] -ceq 'set') {
        $global:privacyMaskGuideFixture.UploadAttempts++
        Assert-SecretGuide (($arguments -join ' ') -ceq
            'secret set PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM --repo mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps --env privacy-mask-release') 'fixed upload target; no key argument'
        Assert-SecretGuide ((@($input) -join "`n") -ceq $global:privacyMaskGuideFixture.Pem) 'private input travels only through stdin'
        if ($global:privacyMaskGuideFixture.Case -ceq 'upload-failed') { $global:LASTEXITCODE = 1 }
        return
    }
    throw 'Unexpected GitHub write; fixture never invokes the real CLI.'
}

Push-Location $repoRoot
try {
    foreach ($case in @('success', 'unsafe-environment', 'late-unsafe-environment', 'wrong-account',
        'wrong-key-id', 'inventory-unavailable', 'existing-secret', 'incomplete-inventory', 'missing-openssl',
        'missing-file', 'cancel', 'decrypt-failed', 'empty-key', 'oversize-key', 'encrypted-key',
        'multiple-keys', 'derive-failed', 'wrong-key', 'upload-failed')) {
        $global:privacyMaskGuideFixture.Case = $case
        $global:privacyMaskGuideFixture.UploadAttempts = 0
        $global:privacyMaskGuideFixture.EnvironmentReads = 0
        $global:LASTEXITCODE = 0
        $failed = $false
        $captured = @()
        try { $captured = @(& $guideAction *>&1) }
        catch { $failed = $true; $captured += $_.Exception.Message }
        Assert-SecretGuide ($failed -eq ($case -cne 'success')) "expected result: $case"
        $expectedAttempts = if ($case -cin @('success', 'upload-failed')) { 1 } else { 0 }
        Assert-SecretGuide ($global:privacyMaskGuideFixture.UploadAttempts -eq $expectedAttempts) "upload admission: $case"
        Assert-SecretGuide (($captured -join "`n") -notmatch 'BEGIN PRIVATE KEY|VEVTVA==') "no private output: $case"
    }
    Write-Host "Passed $($global:privacyMaskGuideFixture.Checks) owner secret-input guide checks; synthetic text only, no key, network or secret writes."
}
finally {
    Pop-Location
    Remove-Variable -Name privacyMaskGuideFixture -Scope Global
}
