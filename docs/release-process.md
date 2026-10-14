# Release Process

Generated Windows executables are distributed through GitHub Releases. They are
not committed to the source repository.

## Version policy

The source currently prepares `2.0.0-beta.2`. This is a testing delivery, not a
stable release announcement. Publishing/tagging remains a separate authorized
action after merge and qualification. A commit or PR is not itself a release.
Earlier source builds displayed 2.0.0 while still unreleased; the beta suffix
clarifies testing status and does not remove or roll back those features.

Use [Semantic Versioning](https://semver.org/) for the product's documented
compatibility contract: compatible fixes increment patch, compatible features
increment minor, incompatible changes increment major. A visual redesign alone
does not automatically require a new major version. The already agreed 2.0
development line remains the target here.

- During development, use `2.0.0-beta.1`, `beta.2`, etc. Before each subsequent
  completed source-delivery PR that changes the shipped app, advance the beta
  counter and numeric file revision together. Do not bump for every commit,
  rebuild, or docs-only PR. The first beta includes the accumulated 2.0 work.
- When the agreed feature scope is complete, prepare `2.0.0-rc.1` for final
  qualification; additional corrected candidates use `rc.2`, etc.
- Only after the live release gates pass, remove the suffix for stable `2.0.0`.
  Later compatible fixes/features use `2.0.1`/`2.1.0` as appropriate.
- Never reuse a published version/tag for different app contents. A local
  rebuild of the same committed source keeps its product version. Git build
  metadata identifies its committed source, not uncommitted local edits.

`Directory.Build.props` is the version source. The helper validates identity
coherence; counters are deliberate reviewed changes, not automatically assigned
on merge or by a Git hook.

| Identity | Current value | Purpose |
| --- | --- | --- |
| Version | `2.0.0-beta.2` | UI label, archive filename and exact `v2.0.0-beta.2` tag |
| InformationalVersion | `$(Version)` plus SDK Git metadata when available | Full build provenance; About shows the product before `+` and exposes the full value in its tooltip/help |
| AssemblyVersion | `2.0.0.0` | Numeric assembly identity, stable throughout this prerelease cycle |
| FileVersion | `2.0.0.2` | Numeric Windows file/build revision |

Windows numeric fields cannot contain `beta` or `rc`. Increase FileVersion's
fourth component for the next delivered beta/candidate/stable build of the same
core; do not reset it when dropping a prerelease suffix. For example beta.2 can
use `2.0.0.2`; after more candidates, stable uses the next revision, not `.0`.
After changing the version core, all numeric identities must match that core.
The settings schema has an independent lifecycle and remains 12 for this work.

About and Settings use assembly informational metadata, not the numeric assembly
version as the main product label. The numeric fallback remains for builds
without informational metadata. Source archives without Git may lack a commit
suffix; the UI does not invent one.

Run `scripts/test-version-policy.ps1` for strict product/Windows identity,
beta/rc/stable classification, wrong/stale package and tag rejection, and a
mocked execution of the actual workflow publish command. It makes no network,
clipboard, tag, release or installed-app changes. Local verification and both
CI workflows run it. Package smoke checks compare both actual PE FileVersion
and ProductVersion with the version source, accepting SDK build metadata.

## Create and test a local release package

1. Run `scripts/verify.ps1`.
2. Run `scripts/package-win-x64-release.ps1`.
3. Extract and inspect `artifacts/release/PrivacyMask-<version>-win-x64.zip`.
4. Run `Install-PrivacyMask.cmd` from the extracted package.
5. Confirm the automated package smoke test passed. It verifies the checksum,
   exact archive contents, executable version, both shortcuts, and uninstall.
6. Confirm the app launches from both generated shortcuts.
7. Run `Uninstall-PrivacyMask.cmd` and confirm the app and shortcuts are removed.

The packaging script publishes the self-contained `win-x64` single-file build,
includes the transactional installer and uninstaller, and writes a SHA-256
checksum beside the versioned ZIP.

## Publish a GitHub release

1. Merge the fully verified release changes into `main`.
2. Create an annotated version tag matching the source, such as `v2.0.0-beta.2`, on the release commit.
3. Push the tag to GitHub.
4. The `release` workflow rejects a tag that does not match the product version,
   builds and tests the project, creates the Windows ZIP and checksum, smoke
   tests the package, and publishes both files on the matching GitHub Release.
   Beta/rc suffixes are classified as **Pre-release** with `--latest=false`;
   stable versions use normal release classification. `--verify-tag` prevents
   the publishing command from inventing a missing tag. A failed publish exits
   the job with an error. The `latest` download link is not a beta download link.
5. Download the published ZIP and perform the install/uninstall smoke test.

Example tag commands:

```powershell
git tag -a v2.0.0-beta.2 -m "PrivacyMask v2.0.0-beta.2"
git push origin v2.0.0-beta.2
```

These commands are a release procedure, not commands run by the current PR.
Tagging/publishing a beta does not qualify the stable release. Complete the
remaining live keyboard/PIN/DPI/resource/stability gates in
[gui-qualification.md](gui-qualification.md) before declaring 2.0.0 stable.

## Future improvements

- Implement the staged [in-app update plan](in-app-updates.md), with a
  [signed manifest](update-manifest.md) and qualified
  [launcher/rollback protocol](update-recovery.md).
- Sign release executables and installers
- Add an MSI or MSIX installer

## Update-system preparation (phase 1 only)

The proposed manifest/signature JSON schemas are versioned developer contracts,
not release assets or authentication implementations. Run:

```powershell
pwsh -File .\scripts\test-update-manifest-schema.ps1
```

This check needs PowerShell 7 on a developer/CI machine only, uses synthetic
in-memory metadata, and makes no network, signing, installation, tag or release
changes. Both CI and release workflows run it with `shell: pwsh`. Passing it
does not establish that signatures, downloads, migrations or rollback work.

The publisher still creates only the existing installer ZIP and checksum.
Phase 2 will add protected signing, update-only artifacts and draft-to-publish
gating. Key setup, immutable-release settings, tags and publication require
their own explicit release authorization. Do not publish the synthetic test
metadata or treat a missing update manifest as an unsigned update fallback.

This docs/developer-check delivery keeps `2.0.0-beta.2`, numeric identities and
settings schema 12 unchanged. Apply the existing version policy when a later
PR actually changes the shipped application, and qualify a first manual
bootstrap installation before advertising subsequent in-app updates.
