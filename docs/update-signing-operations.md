# Update signing and release operations

Status: phase 2 originally supplied developer packaging/verification and a draft
publisher. Subsequent deliveries through beta.11 implement discovery, download,
App install UI, authenticated handoff, launcher and recovery. Beta.12 now
[pins the owner's public key](update-public-trust.md), but runtime activation and
live OS qualification remain unconfigured. The current
[signing readiness plan](update-signing-readiness.md) prepares protected admission
and isolated lab packets; it creates no production key, GitHub environment,
repository setting, tag or release.

## Deliberate setup gate

`build/update-release-policy.json` pins the approved public key `release-key-1`
and intentionally retains `minimumWindowsBuild: null`. Production signed packaging
fails closed until the Windows baseline is qualified and configured. App/launcher
also retain a separate disabled compiled activation gate. This blocks live runtime
updates independently of the tag-driven release publisher's missing environment.
The publisher never silently falls back to an unsigned installer-only release.
Normal local installer packaging remains available without a signing key.

An owner must approve the following before an authorized release:

1. Qualify the supported Windows baseline, schema compatibility and source
   migrations. Put the actual minimum build into the policy, not the smoke
   fixture's illustrative 19041. Verify output schema against `AppSettings`.
2. Choose a NIST P-256 key custodian and a separately protected backup/recovery
   process with a restore exercise. For a solo owner these can be the same person,
   but loss of the primary store must not destroy the backup. Generate the
   production key outside development test automation, after separate approval.
   Private input format is one unencrypted PKCS#8 `PRIVATE KEY` PEM block; the
   secret store protects it at rest. This describes signing-process input, not
   backup storage: keep backup files encrypted and decrypt only for approved
   direct secret input, not an unencrypted disk export. Do not save it in this
   checkout, artifacts, test fixtures, tickets, screenshots, chat, shell history,
   or command argv.
3. Commit only its public SubjectPublicKeyInfo DER encoded as canonical Base64
   and an immutable lower-case key ID in the policy through a reviewed PR.
   Example shape below uses a placeholder, not a usable public key:

   ```json
   { "keyId": "release-key-1", "publicKeySpkiBase64": "<reviewed public SPKI Base64>" }
   ```

   The policy contains no private key and accepts no key URL. The first actual
   update client/helper must embed the same reviewed trust roots in its build;
   it may not learn keys from a fetched manifest or dynamically downloaded policy.
4. After separate setup authorization, create GitHub environment
   `privacy-mask-release`. The owner approved the tracked `SoloMaintainer` plan:
   exactly one reviewer, repository owner `mahmoudSaaadallah`; allow self-review
   deliberately so the owner can approve their own runs. This is not independent
   review. Restrict deployments to exactly one tag rule `v*`, with no branch rule;
   disable administrator bypass where available and verify it in the UI. Check
   the exact merged commit/tag/workflow before each manual deployment approval.
   A job's environment name alone does not prove protection. Future independent
   review requires a reviewed policy change and self-review prevention enabled.
5. Store `PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM` as an **environment secret**, not
   a repository-wide secret; set environment variable `PRIVACYMASK_UPDATE_KEY_ID`
   to the matching reviewed ID. Never paste the secret into a command argument
   or stdout. Use the organization's approved secret/vault input mechanism.
6. Complete key custody, incident and normal bridge-rotation procedures in
   [the manifest contract](update-manifest.md). Consider Authenticode separately;
   this manifest signature does not remove Windows reputation warnings.
7. Optionally enable GitHub immutable releases as a separate authorized repository
   settings change, after verifying the complete draft publication flow. Do not
   assume the workflow itself toggles that setting.

Earlier signing tooling kept beta.2; later shipped-App changes advanced source
through beta.11 without configuring production signing. The public-root delivery
advances source to beta.12, with runtime activation still disabled. Never reuse a
published version/tag for new contents.
Setup/tooling does not authorize publishing any version. Prepare the reviewed
public-root bootstrap before promising end-user in-app updates: empty-trust older
clients cannot learn their first key from a fetched manifest.

## Build and validate locally

Requires .NET 10 SDK and PowerShell 7 for update tooling. These are developer
requirements only, not new end-user dependencies. Keep keys out of the
command line. With a clean committed checkout, an approved policy, an approved
process-scoped signing environment and a matching key ID:

```powershell
.\scripts\package-win-x64-release.ps1
pwsh -File .\scripts\package-win-x64-update.ps1 -KeyId release-key-1
pwsh -File .\scripts\test-update-package.ps1
```

The normal publisher checks committed source provenance, exact product/numeric
version, x64 PE architecture, key match, archive/file hashes, exact one-file ZIP,
schema and policy. Existing signed asset filenames are never overwritten by the
tool; choose a fresh dedicated output directory for a deliberate rebuild.
The supplied application must have been built from HEAD; a stale ignored publish
EXE is rejected even if its product version label happens to match.

The tool is under `tools/PrivacyMask.ReleaseTool`, not referenced by the app,
and is not included in the end-user installer or update ZIP. Keys are read only
by the pack command from process environment. Verify needs only public policy.
Private strings necessarily exist briefly in signing-process memory; disposing
key objects and removing the child environment do not claim to erase every
managed-string copy or defend against a compromised runner/administrator.

## Automated tag release flow

1. Human approval/version qualification and merge happen first. An explicitly
   authorized exact product tag is pushed on the merged commit.
2. `release.yml` uses full Git history and per-tag serialization. A separate
   no-environment preflight checks source/tag and GET-only environment/reviewer/tag
   configuration. The protected signing job `needs` that preflight. Missing or
   mismatched environment configuration blocks admission rather than allowing
   GitHub to create an unprotected environment implicitly. The protected job
   repeats the source gate before its signing step receives the secret. Approval
   remains GitHub's manual deployment gate; the diagnostic does not prove approval,
   bypass settings, immutable settings or key custody.
3. Build/test normal application and installer ZIP. Run update schema, signature,
   archive, source-gate, publisher and ephemeral real-EXE checks.
4. Expose the private secret only to the signed-package step. Missing/invalid
   key, empty/unqualified policy or stale EXE stops the job. No generated test
   key or unsigned fallback is allowed.
5. Independently validate generated signed assets with the public policy.
6. The publisher rechecks the remote tag (including annotated tags) against the
   verified checkout commit, creates a Draft with exactly five assets, and
   compares every remote uploaded asset's name/size/SHA-256 digest/state with
   the local files. Recheck tag identity before publication.
7. Only then clear Draft. Beta/rc is prerelease and not Latest; a stable release
   is explicitly Latest. Confirm the expected published state from the API.

The five files are the normal installer ZIP/checksum, app-only update ZIP,
`update-manifest.json`, and `update-manifest.sig.json`. Scripts/settings/launcher
are never allowed inside the v1 update ZIP. Public release notes are display
text, not instructions to the updater.

## Failure handling and operator recovery

- No required asset/key/configuration: fail before publication. Normal manual
  installation does not imply the signed publishing gate passed.
- Upload failure, wrong/missing digest, duplicates or source/tag change: leave
  the draft unpublished. Inspect the run/draft before an explicitly authorized
  retry. The script deliberately does not overwrite assets, delete releases,
  move tags, or resume an unknown partial draft automatically.
- Publication/API-confirmation failure: inspect actual remote state first. The
  edit may have succeeded despite losing its response; do not blindly publish
  another release or claim rollback of a public release.
- A published immutable release must not be patched in place. Correct the
  source and publish a new reviewed version after normal qualification.
- A compromised signing key requires the separate incident/verified repair
  process. A valid signature does not fix a compromised source or runner.

## Verification without production secrets

```powershell
.\scripts\verify.ps1
.\scripts\test-release-environment.ps1
.\scripts\test-update-qualification.ps1
pwsh -File .\scripts\test-update-manifest-schema.ps1
.\scripts\package-win-x64-release.ps1 -SkipTests
pwsh -File .\scripts\test-signed-update-package.ps1
```

Unit tests generate P-256 keys in memory; the real-EXE smoke suite writes only
public fixture metadata into a scoped temporary directory. It verifies an actual
published executable, modified manifest/archive rejection, refusal to overwrite
assets, wrong source/key rejection and cleanup. It neither installs the update
nor publishes a GitHub release. Publisher/source tests intercept every GitHub/Git
command and exercise failure gates without external writes. Windows PowerShell
5.1 remains supported for the existing installer and source/publisher tests;
PowerShell 7 is used explicitly for cryptographic/schema packaging checks.

Prepare, but do not execute, live lab cases using the
[qualification runbook](update-qualification-runbook.md). Packet generation starts
all 25 cases on each of Windows 10/11 at `NotRun`; it neither provisions a VM nor
certifies an update.

## References

- [GitHub environment protections](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)
- [GitHub release edit flags](https://cli.github.com/manual/gh_release_edit)
- [.NET explicit signature format](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa.signdata?view=net-10.0)
