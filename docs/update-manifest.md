# Signed update manifest contract, version 1

Status: wire contract implemented by phase-2 tooling and phase-3 shared public
verifier/discovery library. App integration and production signed release remain
pending. JSON schemas check structure only; they do not authenticate an update.
See [the delivery plan](in-app-updates.md) and [recovery rules](update-recovery.md).

## Release assets

A discoverable updater release must have exactly one asset with each name:

```text
PrivacyMask-<version>-win-x64-update.zip
update-manifest.json
update-manifest.sig.json
```

The existing `PrivacyMask-<version>-win-x64.zip` and `.sha256` remain available
for first installation/manual repair. Do not download GitHub's source archives
as application updates. Publish all validated assets together from a draft.
Asset digest supplied by GitHub is useful corroboration, not a trust root.

## Trust and byte-level signature format

1. Limit manifest HTTP bodies to 64 KiB and signature envelopes to 4 KiB,
   including after HTTP decompression. Require valid UTF-8 without a BOM.
2. Verify the signature of the exact manifest bytes before interpreting them
   as actionable metadata. Sign every byte, including a final newline if one
   exists. Neither publisher nor client may reserialize after signing.
3. The detached envelope has exactly `algorithm`, `keyId`, and `signature`.
   Algorithm is `ECDSA-P256-SHA256`. Signature is standard padded Base64 of
   exactly 64 bytes in IEEE P1363 fixed-field `r || s` format, not DER.
4. Resolve keyId against public keys pinned in the installed trusted client
   and helper. Never accept a public key, algorithm override or key URL from
   GitHub metadata, a manifest, local journal, or network response.
5. Reject unknown/revoked key IDs, invalid signature, malformed UTF-8,
   duplicate JSON properties at any depth, trailing JSON values, and unknown
   properties. Parsing must retain exact casing and reject lossy coercion.
6. Validate structure using the bundled contract (no remote schema fetch),
   then apply the cross-field and local-state checks below. The worker repeats
   authentication and payload validation before any file mutation.

Recommended .NET verification uses ECDsa with SHA256 and the explicit
`DSASignatureFormat.IeeeP1363FixedFieldConcatenation` overload. This is update
artifact authentication, not Windows Authenticode signing. SHA-256 text alone
does not establish publisher identity.

The publisher keeps its private key out of the repo, application, artifacts and
logs. Use a protected release environment with explicit approval and restrict
signing to approved merged source/tag identity. Never expose release keys to
PR builds, fork workflows, or downloaded/unreviewed build scripts. Document a
backup custodian and incident procedure before publishing updater-capable builds.

## Manifest fields

The machine-readable schemas are
[manifest](contracts/update-manifest.schema.json) and
[signature envelope](contracts/update-signature.schema.json), using JSON Schema
draft-07 with only local references. Product versions are strict SemVer without
build metadata, consistent with the existing release identity policy.

| Field | Contract |
| --- | --- |
| `schemaVersion` | Integer 1; unsupported formats fail closed |
| `appId` | Exactly `PrivacyMask.Desktop` |
| `repository` | Exactly `mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps` |
| `version` / `tag` | Product version and its exact `v<version>` tag |
| `commitSha` | 40 lower-case hexadecimal source commit characters |
| `channel` | `stable` for no prerelease suffix; otherwise `preview` |
| `runtime` | Exactly `win-x64` |
| `minWindowsBuild` | Qualified minimum Windows build, checked against the actual OS |
| `minUpdaterProtocol` | Positive integer; local protocol must meet it; v1 implementation supports protocol 1 |
| `package` | Exact asset name, positive byte size and lower-case archive SHA-256 |
| `files` | Exactly one record for `PrivacyMask.App.exe`, with size and SHA-256 |
| `settingsCompatibility` | Minimum/maximum readable input schema and schema produced by the new application |

Version 1 update ZIPs have one root regular file, `PrivacyMask.App.exe`, with
no additional files/directories, script, launcher, settings or symlink. Do not
execute its entry point as part of signature inspection. Archive size and the
single extracted file are each capped at 512 MiB; allow at most 100:1 expansion.
Those are defensive ceilings, not measured application sizes or RAM budgets.
Use checked 64-bit arithmetic and independently enforce quotas while streaming.

## Required semantic checks (outside JSON Schema)

- Version must be strictly greater than the installed version. Prerelease
  numeric identifiers compare as numbers, not strings; build metadata is not
  precedence. An equal version with a new hash/commit is forbidden.
- Tag equals `v` plus signed version; package name equals
  `PrivacyMask-` plus signed version plus `-win-x64-update.zip`.
- Signed channel matches version suffix. GitHub's prerelease classification
  must agree. Stable users reject preview; preview users can accept newer
  stable. A date, unsigned label or GitHub latest flag cannot authorize downgrade.
- Release is published, not draft, and its required assets are uploaded and
  unambiguous. Signed repository identity matches the fixed discovery endpoint.
- Pin the selected release/tag/asset identities for the whole operation; do
  not accidentally download the asset of a later check. Reject inconsistent
  metadata during download. Release notes are never executable instructions.
- Package size/hash match the signed metadata. Expanded size/hash and exact
  archive entries match `files`. Reject duplicate entries, Windows
  case-insensitive name collisions, absolute/UNC/drive paths, backslashes,
  `..`, streams (`:`), links, reparse points and unexpected names. Stage in an
  owned isolated directory and recheck before apply to limit substitution races.
- Executable is the expected product and x64 architecture; product/core and
  Windows numeric version fields agree with the signed version. Source metadata
  must agree with commitSha when present. No file-name-only validation.
- Actual Windows baseline, local updater protocol and the saved input settings
  schema meet compatibility constraints. `minimumReadableSchema <=
  maximumReadableSchema`; outputSchema must be within that readable interval.
  Compatibility claims do not substitute for tested migrations.
- Unsupported/missing metadata means "release not eligible for in-app update",
  not permission to fall back to an unsigned ZIP or downloaded installer script.

Source archives and builds lacking a product provenance identity must not be
misidentified as a supported managed release. The actual verified Windows
minimum is a release qualification decision, not guessed from an example JSON.

## Transport, retries and storage

Use HTTPS only, a fixed GitHub repository API and its expected asset routes.
Validate every redirect against an explicit GitHub download-host policy,
including HTTPS, expected port and no credentials in URLs. Do not use a suffix
test that accepts attacker domains. Host policy changes require reviewed tests.
No application token is needed for public releases. A private repo would need
a separately designed authentication system, never a shipped shared PAT.

Download to a new per-operation partial file with bounded buffers, timeouts,
size checks and SHA-256 hashing. Partial files are not installable. V1 restarts
an interrupted download; resume/range support is out of scope. A cached verified
package may be applied within 24 hours of its successful release check; older
packages require a new check before handoff. Never weaken signature checks to
work offline. This freshness rule does not claim complete remote revocation.

## Key rotation and compromise boundaries

Normal rotation: first bridge every verifier with the next public key using an
already trusted distribution path; only then publish using the new key. Key IDs
are immutable. With protocol-v1, App-only updates cannot change launcher trust.
A launcher without the next root requires a qualified complete installer update;
shipping only a bridge App does not suffice. See [public trust](update-public-trust.md).
Old clients that do not trust a key cannot install its releases; give a manual
bootstrap route rather than learning keys from an untrusted response.

Compromise is different: an attacker controlling a trusted private key can sign
malicious newer versions. Signature verification alone cannot solve this. Stop
publishing, revoke signing access, investigate source/artifacts, communicate
through an independently trusted channel, and provide an explicitly verified
repair distribution. Offline/old clients cannot magically learn revocations.
Immutable releases and protected signing reduce attack surfaces but do not
replace key custody or protect against every compromised maintainer/CI action.

## Phase-1 verification boundary

`pwsh -File scripts/test-update-manifest-schema.ps1` exercises the schemas with
synthetic metadata; it requires PowerShell 7's Test-Json, not an end-user
dependency. CI runs the same checks. Passing these cases proves structural
contract behavior only. Phase-2 tooling separately tests actual exact-byte pinned
signatures, strict duplicate-key parsing, semantic identity checks and actual
EXE/ZIP validation with memory-only test keys. Phase 3 adds bounded read-only HTTP
fixtures sharing public verification. ZIP download, installed-client activation,
restart and crash recovery remain later phases. None is proven by schema
checks or a passing publisher-only test. See [signing operations](update-signing-operations.md).

## References

- [GitHub release assets and API](https://docs.github.com/en/rest/releases/releases)
- [.NET ECDsa signature verification](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa.verifydata?view=net-10.0)
- [Semantic Versioning](https://semver.org/)
