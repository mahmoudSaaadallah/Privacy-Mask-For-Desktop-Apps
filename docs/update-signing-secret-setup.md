# Owner-operated signing secret input

This is a manual owner action, not an agent-run setup script or publication
approval. Never send the private PEM, passphrase, backup link or token to chat,
logs or a PR. Keep the backup encrypted. The agent has not opened the owner's key
or executed these instructions against it.

The release workflow expects environment secret `PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM`
to contain **one unencrypted PKCS#8 `PRIVATE KEY` PEM block**. Uploading the
`ENCRYPTED PRIVATE KEY` backup unchanged will not work. The public environment
variable `PRIVACYMASK_UPDATE_KEY_ID=release-key-1` is already configured.

## Preconditions

- Review and merge the strengthened environment guard first. Use a trusted local
  checkout and an ordinary interactive PowerShell 7 terminal that the owner alone
  controls. Do not run this via an agent, CI, screen recording, debug trace or a
  session that records process output. Do not turn off organization-mandated
  auditing; use an approved secret-input workstation instead.
- Confirm GitHub account/recovery protection and private encrypted-backup access.
  Authenticate `gh` yourself as repository owner `mahmoudSaaadallah`; this guide
  never retrieves a token from Git credentials or prints one.
- Have OpenSSL installed (the Git for Windows bundled copy is sufficient) and
  know the path to your encrypted PEM **file**, not its parent directory.
- Run the environment guard and inspect the environment UI. Reviewer must be the
  owner only, self-review allowed, administrator bypass off and the sole ref rule
  tag `v*`. Secret access is confined to this environment; approval remains manual.
- If this secret name already exists, stop. Replacement/rotation is a separate
  reviewed action; do not run these creation instructions to overwrite it.

## Direct input without plaintext files or clipboard

Run this only after approving the private-key transfer to the named GitHub
environment. Review every line first. Enter the encrypted file path at the
prompt; OpenSSL asks for its passphrase interactively, not as a command argument.
Only public metadata and success/failure text should appear on screen.

```powershell
$ErrorActionPreference = 'Stop'
$signingRepository = 'mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps'
$signingEnvironment = 'privacy-mask-release'
$signingSecretName = 'PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM'
$decryptedSigningPem = $null
$derivedPublicPem = $null
try {
    ./scripts/assert-release-environment.ps1 -Repository $signingRepository | Out-Null
    $signingAccount = gh api user --jq .login
    if ($LASTEXITCODE -ne 0 -or $signingAccount -cne 'mahmoudSaaadallah') { throw 'Wrong GitHub account.' }
    $signingVariables = gh api "repos/$signingRepository/environments/$signingEnvironment/variables/PRIVACYMASK_UPDATE_KEY_ID"
    if ($LASTEXITCODE -ne 0 -or ($signingVariables | ConvertFrom-Json).value -cne 'release-key-1') { throw 'Key ID variable mismatch.' }
    $signingSecrets = gh api "repos/$signingRepository/environments/$signingEnvironment/secrets?per_page=100"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect secret names.' }
    $signingSecretMetadata = $signingSecrets | ConvertFrom-Json
    if ($signingSecretMetadata.total_count -gt 100 -or
        @($signingSecretMetadata.secrets | Where-Object name -eq $signingSecretName).Count -ne 0) {
        throw 'Existing or incomplete secret inventory; no overwrite allowed.'
    }
    $signingOpenSslCommand = Get-Command openssl -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $signingOpenSsl = if ($signingOpenSslCommand) { $signingOpenSslCommand.Source } else { 'C:\Program Files\Git\usr\bin\openssl.exe' }
    if (-not (Test-Path -LiteralPath $signingOpenSsl -PathType Leaf)) { throw 'OpenSSL not found.' }
    $encryptedSigningPath = (Read-Host 'Full path to the encrypted signing PEM file').Trim().Trim('"')
    if (-not (Test-Path -LiteralPath $encryptedSigningPath -PathType Leaf)) { throw 'Encrypted PEM file not found.' }
    $signingConfirmation = Read-Host "Type UPLOAD to send the decrypted key only to $signingRepository / $signingEnvironment"
    if ($signingConfirmation -cne 'UPLOAD') { throw 'No secret was uploaded.' }
    $decryptedSigningPem = (& $signingOpenSsl pkey -in $encryptedSigningPath -outform PEM) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $decryptedSigningPem.Length -gt 16384 -or
        $decryptedSigningPem -cnotmatch '\A-----BEGIN PRIVATE KEY-----\s+[A-Za-z0-9+/=\s]+-----END PRIVATE KEY-----\s*\z') {
        throw 'Decryption failed or unexpected private PEM; no secret was uploaded.'
    }
    $derivedPublicPem = ($decryptedSigningPem | & $signingOpenSsl pkey -pubout 2>$null) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Public identity derivation failed; no secret was uploaded.' }
    $derivedSigningSpki = $derivedPublicPem -replace '-----BEGIN PUBLIC KEY-----|-----END PUBLIC KEY-----|\s', ''
    $approvedSigningSpki = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAET51tmvseX6BRpk7j0M+xCnvYyMogUFwUBHYBGaw6MZeoA26Dm/GgGX/bGHr9PVej4XPOR4QTUnoiKlALmTnZ4A=='
    if ($derivedSigningSpki -cne $approvedSigningSpki) { throw 'Wrong signing key; no secret was uploaded.' }
    ./scripts/assert-release-environment.ps1 -Repository $signingRepository | Out-Null
    $decryptedSigningPem | gh secret set $signingSecretName --repo $signingRepository --env $signingEnvironment
    if ($LASTEXITCODE -ne 0) { throw 'Secret upload was not confirmed; inspect metadata before retrying.' }
    Write-Host 'Signing secret uploaded. No release or runtime activation was performed.'
}
finally {
    $decryptedSigningPem = $null
    $derivedPublicPem = $null
}
```

The plaintext exists briefly in PowerShell/OpenSSL/`gh` process memory and the
stdin pipe. Setting variables to null is not guaranteed memory erasure and does
not defend against a compromised workstation or administrator. No plaintext file,
clipboard or private command argument is needed. GitHub CLI encrypts the input
locally before sending it to GitHub; the signing runner later receives it as a
secret, so workflow review and environment protections still matter.

The name inventory is a point-in-time guard, not an atomic create-only API. Do
not let another operator create or rotate this secret concurrently. If a failure
occurs during upload, inspect names/timestamps before deciding whether to retry;
never fetch or print a secret value to verify it.

## Verification and next boundary

Use `gh secret list --repo mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps --env privacy-mask-release`
to confirm the **name and timestamp only**. Presence does not prove key contents
or successful signing. A future separately approved qualified signing run must
verify its public identity and signed assets. Do not create a test tag or release
merely to check this setup. Runtime activation remains disabled and the supported
Windows baseline remains unqualified.

References: [GitHub CLI secret stdin and local encryption](https://cli.github.com/manual/gh_secret_set),
[environment protection and secret access](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments),
[OpenSSL pkey output formats](https://docs.openssl.org/3.0/man1/openssl-pkey/).
