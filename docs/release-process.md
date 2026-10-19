# Release Process

Generated Windows executables are distributed through GitHub Releases. They are
not committed to the source repository.

## Version policy

The source currently prepares `2.0.0-beta.15`. This is a testing delivery, not a
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
| Version | `2.0.0-beta.15` | UI label, archive filename and exact `v2.0.0-beta.15` tag |
| InformationalVersion | `$(Version)` plus SDK Git metadata when available | Full build provenance; About shows the product before `+` and exposes the full value in its tooltip/help |
| AssemblyVersion | `2.0.0.0` | Numeric assembly identity, stable throughout this prerelease cycle |
| FileVersion | `2.0.0.15` | Numeric Windows file/build revision |

Windows numeric fields cannot contain `beta` or `rc`. Increase FileVersion's
fourth component for the next delivered beta/candidate/stable build of the same
core; do not reset it when dropping a prerelease suffix. For example beta.2 can
use `2.0.0.2`; after more candidates, stable uses the next revision, not `.0`.
After changing the version core, all numeric identities must match that core.
The settings schema has an independent lifecycle and remains 13 for this delivery.

About and Settings use assembly informational metadata, not the numeric assembly
version as the main product label. The numeric fallback remains for builds
without informational metadata. Source archives without Git may lack a commit
suffix; the UI does not invent one.

Run `scripts/test-version-policy.ps1` for strict product/Windows identity,
beta/rc/stable classification, wrong/stale package and tag rejection, and
publishing-workflow wiring. `test-release-publication.ps1` separately mocks the
actual draft publisher and `test-release-source.ps1` mocks source gates. None
makes network, clipboard, tag, release or installed-app changes. Local
verification and both CI workflows run them. Package smoke checks compare PE FileVersion
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

The packaging script publishes the self-contained `win-x64` single-file App and
Native-AOT stable launcher, includes the transactional installer and uninstaller,
and writes a SHA-256 checksum beside the versioned ZIP. Publishing the launcher
from source requires the Visual Studio Windows C++ linker workload; installing
the finished ZIP does not.

## Publish a GitHub release

First complete [protected signing setup](update-signing-operations.md). The
committed policy pins the owner's public key and uses a Windows 11 technical
build filter for the owner-approved [Preview A/B trial](update-preview-trial.md).
Stable runtime and stable signing-job admission remain disabled; the trial is
not full OS qualification. This is not permission to reuse an existing version.
The [signing readiness plan](update-signing-readiness.md) records the owner's
SoloMaintainer choice, separate setup approvals and real-App lab gates. A
GET-only no-environment preflight now blocks signing-job admission if the existing
reviewer/tag configuration is missing or mismatched; naming an environment alone
is insufficient. The plan does not create it or authorize a tag push.

1. Merge the fully verified release changes into `main`.
2. After exact source/tag approval, create an annotated version tag matching the source, such as `v2.0.0-beta.15`, on the release commit.
3. Push the tag to GitHub.
4. The `release` workflow requires the named protected environment and a clean
   merged exact version tag, builds/tests the project and normal Windows ZIP,
   creates an app-only update ZIP plus signed manifest with the approved key,
   and independently verifies them. It creates a Draft containing all five
   assets only if no release/draft already exists for the tag. It resolves the
   created URL to exactly one numeric release ID across authenticated API pages,
   checks uploaded sizes/SHA-256 digests/identities and source tag, then publishes
   and confirms that exact ID. Draft reads never use the published-only tag
   endpoint, and publication never uses an ambiguous tag-based edit. Beta/rc
   remains **Pre-release** with `make_latest=false`;
   stable publishing is blocked by the source gate during this Preview trial.
   General stable/Latest classification is retained for a future qualified rollout.
   `--verify-tag` prevents inventing a missing tag.
   Missing signing configuration or any failed gate stops publication; upload
   failure may leave an unpublished draft for explicit operator inspection.
5. Download the published ZIP and perform the install/uninstall smoke test.

Example tag commands:

```powershell
git tag -a v2.0.0-beta.15 -m "PrivacyMask v2.0.0-beta.15"
git push origin v2.0.0-beta.15
```

These commands are a release procedure, not commands run by the current PR.
Tagging/publishing a beta does not qualify the stable release. Complete the
remaining live keyboard/PIN/DPI/resource/stability gates in
[gui-qualification.md](gui-qualification.md) before declaring 2.0.0 stable.

If publication fails after draft creation, inspect remote state rather than
rerunning the workflow. A tooling fix merged after a tag does not change that
tag's workflow. Never move the tag to pick up the fix or blindly publish by tag.
See the [beta.14 publication incident](release-publication-incident.md).

## Future improvements

- Implement the staged [in-app update plan](in-app-updates.md), with a
  [signed manifest](update-manifest.md) and qualified
  [launcher/rollback protocol](update-recovery.md).
- Sign release executables and installers
- Add an MSI or MSIX installer

## Update-system preparation (phases 1 and 2)

The proposed manifest/signature JSON schemas are versioned developer contracts,
not release assets or authentication implementations. Run:

```powershell
pwsh -File .\scripts\test-update-manifest-schema.ps1
```

This check needs PowerShell 7 on a developer/CI machine only, uses synthetic
in-memory metadata, and makes no network, signing, installation, tag or release
changes. Both CI and release workflows run it with `shell: pwsh`. Passing it
does not establish that signatures, downloads, migrations or rollback work.

Phase 2 implements signed update-only packaging, offline verification and a
tested draft publisher in isolated developer tooling. Run real-EXE tests with
`pwsh -File scripts/test-signed-update-package.ps1` after publishing the current
committed EXE. The tests use ephemeral memory-only private keys, not production
credentials. Configuration/custody/OS gates remain deliberately pending; key
setup, immutable-release settings, tags and publication require separate
explicit authorization. Do not publish synthetic test metadata or treat a
missing update manifest as an unsigned fallback. The app does not activate the
discovery client during the earlier tooling deliveries. Beta.8 now adds the
manual Settings/tray UI, still blocked before HTTP by empty compiled trust.

The earlier signing/tooling deliveries kept beta.2 unchanged. The
[awaited shell lifecycle delivery](update-shell-lifecycle.md) advanced the shipped
application to beta.3. The [authenticated App readiness delivery](update-app-readiness.md)
adds a shipped startup/migration path and advances to beta.4 / FileVersion 2.0.0.4,
without changing settings schema 12 or activating update discovery/UI. The
[stable launcher and installer migration](update-stable-launcher.md) advances to
beta.5 / FileVersion 2.0.0.5, without changing settings schema 12 or activating
update discovery/UI. Qualify this manual migration before advertising subsequent
in-app updates.

The later [authenticated handoff contract](update-handoff.md) is an internal,
unreachable App/launcher protocol and therefore keeps beta.5/FileVersion 2.0.0.5
unchanged. A subsequent delivery that activates it in the shipped runtime must
advance the prerelease/file revision under the policy above.

The [runtime handoff delivery](update-runtime-handoff.md) advances source to
beta.6 / FileVersion 2.0.0.6 because it changes shipped App/editor lifecycle and
adds the launcher's authenticated apply route. AssemblyVersion stays 2.0.0.0
and settings schema stays 12. Compiled trust roots are still empty, so this does
not activate network checks or end-user update installation.

The [bounded storage maintenance delivery](update-retention.md) advances source
to beta.7 / FileVersion 2.0.0.7 for shipped launcher cleanup, staging activity
locks and bounded recovery scratch. AssemblyVersion remains 2.0.0.0 and settings
schema remains 12. No tag or Release is published by this source-delivery PR.

The [Settings/tray manual discovery delivery](update-settings-ui.md) advances to
beta.8 / FileVersion 2.0.0.8 and settings schema 13. It persists update
preferences and exposes manual check status without background checks or
download/apply activation. Public-key custody/OS qualification still block live
signed discovery. Existing synthetic schema-12 fixtures continue to exercise
older-input compatibility; current release policy/output is schema 13.

The [durable startup-discovery delivery](update-check-scheduling.md) advances to
beta.9 / FileVersion 2.0.0.9 with schema-13 user settings and independent schema-1
bounded check history. It adds one-shot scheduling and persistent retry/admission
without configuring production keys, publishing a tag/Release, or connecting
the download/apply UI. Compiled trust remains empty.

The [explicit verified-download UI delivery](update-download-ui.md) advances to
beta.10 / FileVersion 2.0.0.10 without changing settings/history schemas. Settings
now connects signed download/progress/cancel/owned-stage discard to the managed
stager and durable single-flight retry session. No installation/restart UI,
production trusted key, tag, Release or live activation is introduced. Staging
tests are not real-App A-to-B or Windows 10/11 physical-fault qualification.

The [owned-stage installation UI delivery](update-install-ui.md) advances to
beta.11 / FileVersion 2.0.0.11 with unchanged user/history schemas. Update now
connects the returned owned stage to existing consent/Panic/edit and authenticated
restart gates, with a separate safe-to-close boundary to prevent self-deadlock.
App/Native AOT launcher apply checks share durable GitHub retry history, and
worker compatibility uses pinned prepared settings rather than assuming schema 12.
Production trust, activation and live real-App/OS/fault qualification remain
unapproved. Publishing a tag/Release is still a separate authorized action.

The [owner public-key bootstrap delivery](update-public-trust.md) advances to
beta.12 / FileVersion 2.0.0.12. It pins the same owner-approved P-256 public root in
publisher policy and the shared App/launcher library. A separate compiled runtime
activation gate remains false, and the Windows baseline stays unqualified. Public
pinning neither starts HTTP nor authorizes a signing secret, environment change,
tag, release, installed-App update or live qualification claim.

The [required automatic update delivery](update-mandatory.md) advances to beta.13 /
FileVersion 2.0.0.13. After separately approved activation it checks automatically,
downloads without a cancellation choice and applies without confirmation when
Panic, edits and visible protected windows permit a safe restart. Daily successes,
bounded failure retries and shared server deadlines survive restarts. Runtime
activation and signed-publication/OS/bootstrap qualification remain separate gates.

The [beta.14 Preview experiment](update-preview-trial.md) separately enables
managed Windows 11 x64 prerelease operations and a beta.14-only 10-minute
successful-check cadence. Beta.15+ defaults to daily; failure/server retry and
privacy/recovery gates remain. Protected source admission blocks stable tags.
The Windows build floor is a technical Preview filter, not completed qualification;
exact tag approval, real signing/publication and consenting-device A/B evidence
remain separate from source delivery. The beta.12/.13 statements above are history.
