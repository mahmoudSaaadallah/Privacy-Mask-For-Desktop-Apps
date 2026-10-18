# In-app updates: delivery plan and architecture

Status: phase 1 specifications, phase 2 signing/packaging tooling, phase 3
[read-only discovery](update-discovery.md), phase 4 [verified staging](update-download.md),
and internal phase-5a/b/c.1/c.2 recovery/prototype/[preparation](update-preparation.md)/
[transaction](update-transaction.md)
slices and the [phase-5d.1 awaited shell lifecycle](update-shell-lifecycle.md)
foundation plus [phase-5d.2 authenticated App readiness](update-app-readiness.md)
and [phase-5d.3 stable launcher/migration](update-stable-launcher.md) are
implemented, along with the internal
[phase-5d.4 authenticated handoff contract](update-handoff.md) and
[phase-5d.5 runtime handoff](update-runtime-handoff.md) plus
[phase-5d.6 bounded storage maintenance](update-retention.md). Phase 5 as a
whole is not complete.
Production signing/environment/OS qualification remain unset;
see [signing operations](update-signing-operations.md). The subsequent
[phase-6a Settings/tray delivery](update-settings-ui.md) adds manual checks and
preferences in `2.0.0-beta.8`. Empty compiled trust returns NotConfigured before
network or installation inspection. No scheduled check, download/apply UI,
production key, tag or new release is activated.

## Current implementation and gaps

The Windows x64 WPF app is distributed as a self-contained executable. The
default per-user installation and local settings share
`%LocalAppData%\PrivacyMask.Desktop`; settings use `settings.v1.json` and schema
13. The public source is
`mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps` on GitHub.

`Install-PrivacyMask.ps1` now migrates the previous flat executable into the
managed layout, preserves local settings and startup preference, and points
shell entry points at a stable native launcher. Manual installer replacement
still stops the exact installed process and treats restart failure as a warning;
the in-app transaction uses the separate authenticated readiness protocol.

Before phase 2, `release.yml` published the installer ZIP and SHA-256 file on a
matching `v*` tag. The new publisher requires signed app-only assets and a
verified draft; it deliberately cannot publish until its production policy and
environment are configured. This does not add an updater client to the app.
`App.xaml.cs` acquires a session-local singleton before starting the shell;
the update worker must not accidentally reactivate that old instance. Shell
normal shutdown now drains accepted mutations and window event continuations
before disposal/singleton release. An `OnExit` callback is not a shutdown
acknowledgement; the runtime adapter now owns authenticated shutdown and consent.

Panic and pause states are transient. Restart starts ordinary protection, not
the previous opaque panic mask. Update must never be offered as an unlock path.

## Product rules

- Publish a complete GitHub Release, not a commit, branch, source ZIP, or bare
  tag. No update is installed merely because a PR was merged.
- Offer manual checks in Settings and the tray. Default background discovery
  checks once after a short startup delay when the last attempt is at least
  24 hours old. It can be disabled. Installation always requires consent.
- The initial implementation applies only to a verified managed Windows x64
  installation. A source, `dotnet run`, or portable build may display the
  release page but must not replace repository or portable files.
- Require neither administrator rights nor a .NET SDK on a normally installed
  user's machine. Never embed a GitHub access token in the application.
- Preserve profiles, paths, mask settings, hover dimensions, shortcuts, startup
  preferences, onboarding, and the existing PIN verifier.
- Keep the application functional offline. A failed check means "Unable to
  check", not "Up to date". No recurring error modal for background checks.
- Never silently downgrade. Transactional recovery restores exactly the
  version backed up for that operation, not any requested older release.

## Release selection

`Stable` accepts published non-prerelease versions. `Preview` accepts published
prerelease and stable versions. Stable builds initially select Stable; beta/rc
builds initially select Preview with a visible explanation. Preserve a saved
choice across updates. Switching to Stable does not downgrade a beta.

Discover from the paginated releases API of the fixed repository. Ignore
drafts, incomplete uploads, versions outside the supported contract, and
releases with missing/invalid signed metadata. Compare strict SemVer numerically,
including numeric prerelease identifiers; never compare labels lexically or
choose by timestamp alone. Build/source metadata does not affect precedence.
Equal-version replacement is not an update. Release tag, signed product version,
channel, architecture, asset name, and executable metadata must agree.

Use one asynchronous client, bounded JSON responses, timeouts, cached ETags,
single-flight checks, API backoff, and rate-limit headers. A manual click cannot
bypass an active server-imposed retry delay. Bound pagination and report an
incomplete check rather than incorrectly claiming the highest version was
found. Interpret release notes as display-only text, never commands or HTML.

## Component boundaries

| Component | Responsibility |
| --- | --- |
| `GitHubReleaseClient` | Discover releases, HTTP limits, cache and channel filtering |
| `UpdateManifestVerifier` | Fixed trust roots, signature, versions, limits and compatibility |
| `UpdateDownloader` | Stream to disk, cancel, progress and SHA-256 verification |
| `UpdateCoordinator` | Single operation, edit-session decisions, panic and restart consent |
| Installed launcher/recovery worker | Installation lock, journal, file changes, readiness and rollback |

`PrivacyMask.Updates` shares public trust and implements discovery with offline
HTTP fixtures; release tooling alone owns private signing. The App now uses
the library for readiness/handoff and manual checks; production trust remains empty.
Core policy tests remain independent of WPF and HTTP. The App owns UI and shell
integration. Windows-specific process, filesystem, IPC and launcher behavior
belongs outside the Core policy. The worker is short-lived, not a Windows
service. Prototype a self-contained Native/NativeAOT helper and measure its
size before choosing the implementation; do not ship a second full .NET runtime
simply to replace one executable.

## Managed layout and first-time migration

Managed layout (created by the beta.5 installer and used by the production
launcher):

```text
%LocalAppData%\PrivacyMask.Desktop\
  settings.v1.json                 existing data location, unchanged
  settings.v1.json.bak             existing settings recovery copy
  managed-install.json             install identity and supported protocol
  .update.lock                     exclusive updater/installer lease
  update-journal.json              authoritative active recovery operation
  PrivacyMask.Launcher.exe         stable start/recovery entry, outside app swap
  app\PrivacyMask.App.exe          replaceable signed application payload
  update-staging\operation-<id>\    verified ZIP/EXE/signed metadata
  update-operations\operation-<id>\ retained app/settings snapshots and target
```

The marker alone is not an authorization token. Validate the executable's
canonical path, install identity, owner, architecture, permitted roots and
reparse-point policy as well. Non-default custom installations need explicit
qualification before automatic apply is enabled there.

The first updater-capable release uses the existing manual installation
workflow once. Its installer migrates the old root executable into the
managed app layout, preserves settings at their current path, installs the
launcher, updates Desktop/Start Menu shortcuts and the existing HKCU startup
entry without turning startup on for a user who disabled it. The application
must register the launcher, not itself, for subsequent sign-ins. Uninstall and
manual reinstall must honor the same lock/journal and ownership rules.

The migration transaction retains its directory backup until replacement,
shortcut and startup work completes, then restores the prior installation on
failure. Legacy installer scripts pre-dating beta.5 are not a repair path for
the managed layout; reinstall beta.5 or newer instead.

Application updates in protocol v1 replace only the application payload, not
the launcher, installer scripts, or user data. The launcher protocol is versioned
independently. An update requiring a newer protocol must be rejected with an
explicit bootstrap/repair instruction, not bypass the compatibility check.
Safely upgrading the launcher itself is a separate tested protocol extension.

## User journey

1. Open Settings > Updates or use Check for updates in the tray.
2. See current version, Stable/Preview, last successful check, and automatic
   check preference. A newer compatible release shows version, size, notes,
   Update now and Later.
3. Update now downloads and verifies while masks remain active. Cancel remains
   available before application handoff. Network interruption leaves the
   current application unchanged.
4. Before handoff, resolve pending edits with Save / Discard / Cancel. Wait for
   pending settings writes. If any global or profile panic is active, defer
   apply until the user unlocks it through the normal verified PIN path.
5. Explain that masks disappear during restart; the user should close/minimize
   sensitive windows. This is consent to apply, not a promise of gapless masking.
   Recheck panic and edits immediately before taking the handoff lock; cancel
   if conditions changed. Keep a verified download for Later when safe.
6. Show Downloading, Verifying, Installing and Restarting states. After handoff
   do not offer unsafe cancellation or a competing second update.
7. Restart according to the saved minimized preference. Report success only
   after readiness; show an accurate recovery message if the old version was
   restored. If rollback cannot complete, retain evidence/backup and provide
   manual repair instructions without claiming success.

The normal clean-session flow uses one Update now action and its visible
restart warning. Unsaved edits, panic locks and failures can require additional
decisions; "one click" must not remove those safety checks.

## Resource and privacy boundaries

- Stream archives to disk using bounded buffers, not a full byte array in RAM.
- Check space for download, expanded payload, old application, settings backup,
  journal and safety margin. Use the signed file sizes and a bounded expansion
  ratio; stop on disk-full errors even after preflight.
- Prefer a complete verified package initially. Delta updates, ARM64,
  portable self-update, forced update and rollout cohorts are out of v1 scope.
- Keep one previous completed application for recovery; remove interrupted
  downloads with path-scoped cleanup. Never delete an uncommitted operation's
  last viable backup just because a retention timer expired.
- Update requests expose ordinary HTTP connection metadata to GitHub, but
  never profiles, PINs/verifiers, executable paths, window titles, captures or
  content. No telemetry or cloud account is introduced.
- Amend README and About privacy wording in the runtime integration PR, before
  enabling outbound checks. Disabling auto-check prevents background requests;
  manual Check for updates still makes the explicitly requested connection.
- Redact query tokens, paths containing user identities and all credentials
  from bounded local diagnostic logs. No captured frames are written to disk.

## Delivery sequence and gates

| Phase / separate PR | Delivery and exit condition |
| --- | --- |
| 1: specification | This document, manifest contract/schema, recovery/threat model, schema checks; app/version unchanged |
| 2: publisher | Signed update-only artifact; protected signing setup; Draft then publish; fail closed if a key/asset is missing |
| 3: discovery | Tested strict SemVer/channel filtering, HTTP fixtures, cache/backoff and install eligibility; no replacement yet |
| 4: download | Bounded download, progress/cancel, signed verification, safe extraction and disk-failure tests |
| 5: recovery | Measured helper prototype, stable launcher, migration, journal/lock, graceful shutdown, readiness and crash rollback |
| 6: integration | Settings/tray UI, preferences migration, privacy wording, panic/edit protection, new installer/uninstaller compatibility |
| 7: qualification | Real release A-to-B upgrade, fault injection and Windows 10/11 non-admin tests; documentation and release approval |

Each commit contains one coherent change; related tests can accompany its code.
Keep the agreed agent/co-author trailers. Do not publish a tag/release as a side
effect of a documentation PR. A docs/schema-check delivery does not bump the
app version. The first completed PR changing the shipped application follows
the existing beta/FileVersion policy; final stable 2.0.0 still requires all
existing live qualification gates as well as update gates.

Before phase 5 can pass, record evidence for helper size/startup overhead,
Windows baseline compatibility, launcher repair/upgrade boundaries, junction
and locked-file behavior, singleton sequencing, and power-loss recovery.
Before any updater-capable release, approve key custody and revocation/rotation
procedures and complete the recovery matrix. No phase-1 document is evidence
that these guarantees have already been implemented or tested.

Phase 5 is delivered in reviewed slices. The first [recovery kernel](update-recovery-kernel.md)
implements internal journal/lease/rollback primitives only; the launcher,
readiness, migration and real crash/power-loss qualification gates remain open.
The [phase-5b bootstrap/readiness prototype](update-bootstrap-prototype.md) adds
isolated real-process tests and packaging measurements, not runtime activation.
The [phase-5c.1 authenticated preparation](update-preparation.md) now connects
staged signed bytes to exact baseline copies and Prepared journaling, fresh
release selection/space checks and explicit terminal archival. Forward apply,
real app readiness, installer migration and safe bounded pruning remain open.
The [phase-5c.2 transaction worker](update-transaction.md) now exercises exact old
process exit, authenticated forward replacement, test-child startup/commit/receipt
and failure recovery with signed real test processes. It is not wired to the real
App or a production launcher, and does not complete old-version restart or the
runtime consent/Panic/settings integration gates.

The [phase-5d.1 shell lifecycle](update-shell-lifecycle.md) serializes complete
settings mutations and awaits normal shell/window disposal in the actual App,
advancing source to beta.3.

The [phase-5d.2 App readiness path](update-app-readiness.md) parses the private
update-start contract before normal activation, validates the managed layout,
strictly migrates the prepared settings baseline and authenticates Ready/commit/
receipt after singleton, tray, hotkey and first-refresh initialization. It advances
source to beta.4. The [phase-5d.3 stable launcher/migration](update-stable-launcher.md)
makes managed startup and crash recovery reachable from beta.5 installations,
routes shortcuts/startup through the launcher and qualifies legacy migration.
It does not authorize an update, activate discovery/network UI or implement
authenticated old-process exit. The [phase-5d.4 handoff contract](update-handoff.md)
adds strict apply arguments and a mutually authenticated, single-use App/launcher
shutdown exchange. The [phase-5d.5 runtime handoff](update-runtime-handoff.md)
connects it to App consent/Panic/edit and writer-suspension gates and the installed
launcher's apply route. Production trust is still unconfigured; no update UI or
background network path invokes the adapter. The [phase-5d.6 maintenance slice](update-retention.md)
retains a verified recovery pair, quarantines obsolete history/expired staging,
protects live downloads and bounds storage admission/recovery scratch. Unknown
or uncommitted evidence is preserved and can block further attempts at capacity.
Real-App and Windows fault qualification remain required before end-user activation.

## Related specifications and sources

- [Signed manifest contract](update-manifest.md)
- [Read-only discovery and host contract](update-discovery.md)
- [Verified download and staging boundaries](update-download.md)
- [Recovery and threat model](update-recovery.md)
- [Authenticated App readiness](update-app-readiness.md)
- [Release process and version policy](release-process.md)
- [GUI and live release qualification](gui-qualification.md)
- [GitHub release API](https://docs.github.com/en/rest/releases/releases)
- [GitHub API rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api)
- [GitHub immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)
