# Owned-stage installation UI (phase 6d)

Source delivery: `2.0.0-beta.11`, FileVersion `2.0.0.11`, AssemblyVersion
`2.0.0.0`. User settings remain schema 13, history remains schema 1 and managed
updater protocol remains 1. Production compiled trust remains empty: this build
does not activate network checks, downloads or installation for an end user.
No signing credentials, key, tag, Release or installed-user mutation is introduced.

## End-user flow after separately approved activation

1. Check the saved Stable/Preview channel and explicitly download an available
   signed release. Keep protection active while its separate stage is verified.
2. **Update now** appears only for this session's verified ready stage. The
   runtime, not a UI/CLI/settings path, supplies its owned operation ID, signed
   version and captured channel. A different saved channel refuses the selection:
   discard and check/download again. There is no scan-to-apply or offline cached
   stage restoration after restarting the application.
3. The existing restart coordinator rejects shutdown and any global/profile
   Panic, including a profile with no running window. Unlock through the normal
   PIN path first; updating never verifies/changes a PIN or bypasses a lock.
4. Resolve incomplete/invalid editors and pending settings saves using existing
   Save / Discard / Cancel. Then explicitly confirm the warning that masks
   disappear during restart: close or minimize sensitive windows beforehand.
   Cancel/refusal retains the downloaded package without restarting.
5. Suspend and drain accepted shell writers, full window continuations and
   discovery/download work while keeping overlays alive. Recheck Panic, edits,
   closing state and the exact owned selection/saved channel before preparation,
   after preparation and immediately before authenticated handoff acceptance.
6. The App and fixed installed launcher independently verify signed bytes,
   payload/baselines, compatibility, fresh release selection and recovery space.
   Preparation/IPC errors before accepted handoff keep the old App alive. Safe
   journal abandonment permits resume; unconfirmed cleanup leaves writers/UI
   frozen, retains evidence and exposes only ordinary tray Exit for repair.
7. After authenticated acceptance, the shell drains resources and releases its
   singleton. The retained launcher waits for exact clean App exit, replaces
   only the payload and follows the existing Ready/commit/receipt protocol.
   User cancellation cannot resume writes after that boundary. Saved minimized
   startup behavior is preserved. Recovery restarts the verified previous App;
   failures needing repair retain evidence instead of claiming success.

A pre-exit preparation/worker exception can leave a safely retired operation's
ID in recovery history. Do not reuse it for another preparation. The UI requires
discarding that download and checking/downloading a fresh operation (or manual
installation), while preserving the recovery evidence. Pure refusal results
before preparation—Panic, cancelled edits/consent, busy, late edits—retain the
ready download for another explicitly requested attempt.

Ready/download/consent are never reported as installed success. The old UI says
Restarting and explicitly notes that success is not yet confirmed; the worker's
existing readiness/commit and recovery outcomes remain the authority. Production
real-App A-to-B/readiness/recovery qualification is still a release gate.

## Awaited lifecycle without a circular shutdown

`UpdateInstallSession` admits one dispatcher-owned request before binding/event
notifications. Duplicate/reentrant clicks join it. Install-in-progress disables
new install/discard actions. No tray/timer/normal Exit starts installation.

The window raises a non-awaiting install intent; the shell owns its task and
cleanup. Putting that task into the window's download/writer drain would make
update suspension wait for the operation that requested it. Likewise the install
task ultimately awaits `RequestShutdown`, so shutdown must not await that task.

Instead, ordinary shutdown cancels pre-acceptance work and awaits a separate
safe-to-close boundary. That boundary completes only after cleanup has returned,
or the restart coordinator has completed authenticated handoff and explicitly
calls `AcceptHandoff` immediately before requesting shutdown. Accepted work is
not user-cancelled or thawed. Late continuations after accepted handoff do not
rewrite terminal UI status. Tests exercise both ordinary cancellation and the
exact self-shutdown/racing-acceptance shapes, not an unawaited fire-and-forget
disposal substitute.

## Shared retry and compatibility checks

The existing private 4KB history/store moved into `PrivacyMask.Updates` without
changing its file names/schema. Generated JSON metadata, not reflection fallback,
makes it usable by the Native AOT launcher. The App's ordinary discovery/download
session keeps its existing store ownership and 24-hour scheduling policy.

Production apply clients use `GitHubReleaseClient.ForManagedApply`: every App or
launcher fresh check takes the same exclusive history lease, refuses unresolved
pending intent or known global Retry-After, persists attempt before HTTP and
finalizes the result with an independent bounded five-second token. Stable/Preview,
process restart and an Update now click cannot bypass a known server deadline.
Failure to save a result preserves pending evidence and fails closed. Disabling
automatic checks does not revoke an explicitly consented install request.

The worker no longer assumes settings schema 12. It pins and hashes the exact
prepared `original-settings.json` against the journal baseline, then reads its
single positive version field (case-insensitive like the settings store).
Missing, absent, malformed or ambiguous version input fails before requesting
App exit; no guessed compatibility is invented. The target's signed readable
range must include that actual schema. This does not deserialize PINs/profiles,
send settings, or change the settings schema; absence can require manual repair
rather than an unsafe fallback. Synthetic process fixtures now declare their
actual schema-12 test input explicitly.

## Verification and remaining gates

New session tests cover explicit admission, missing trust/stage/channel,
single-flight/reentrancy, each refusal, safe vs unsafe cleanup, ordinary exit
drain, self-shutdown and racing acceptance. Durable request tests cover global
retry, lost result and cancellation while holding the same private lease.
Prepared-schema tests cover current/older versions and ambiguous/missing input.
Compiled WPF checks cover the connected button, no install self-drain, unchanged
settings and available/download/ready/install/restart/cancel/repair layouts.

The isolated signed process matrix, real-EXE offline signed staging tests and
Native AOT publishing are additional evidence, not a claim of live production
installation. Remaining: approve signing custody/compiled roots and bootstrap
eligibility, perform signed real App A-to-B and failed-update rollback, Windows
10/11 standard-user, slow-machine/resource and physical disk-full/power-loss
qualification, then explicitly authorize release publication/activation.
