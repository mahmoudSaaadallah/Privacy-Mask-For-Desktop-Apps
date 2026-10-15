# Transactional replacement and process readiness (phase 5c.2)

Status: **internal worker exercised with signed disposable TEST processes**.
This is not the production launcher or an end-user update feature. The actual
App still has no Updates reference or `--update-start` implementation. The
bootstrap prototype still only accepts `--probe`; the existing installer,
shortcuts/startup, settings schema 12 and product `2.0.0-beta.2`/FileVersion
`2.0.0.2` are unchanged. No production key, release/tag or user install is used.

## Process authority before replacement

`AppProcessExit.Capture` requires the held private managed-install lease and
exact Prepared operation. While the old process is **alive**, it opens/retains
its OS Process/SafeHandle and validates PID, UTC creation time, canonical image
`<root>/app/PrivacyMask.App.exe` and original payload hash. It is not a boolean,
PID-only receipt or a process-name lookup. The retained handle prevents later
identity checks from silently referring to a recycled PID.

`WaitAsync` only awaits that process, with cancellation and a 250 ms–60 s
deadline. It sends no shutdown message, window close, force-kill or tree kill.
An unobserved/live, abnormal nonzero exit, disposed/foreign/consumed handle or
changed operation cannot authorize replacement. The proof is bound to the
install and original immutable journal fields and consumed once at handoff.

**Exit is not consent or proof of clean shell disposal.** The future App/helper
handoff must authenticate/serialize Save/Discard/Cancel, awaited settings writes,
all global/profile Panic/PIN states, restart-gap consent and graceful shell/
singleton shutdown before this worker is armed. Writers must remain frozen;
other launchers/installers/uninstallers must honor the same lease. Those gates,
production shutdown IPC, normal-launch singleton sequencing and boot recovery
are not connected here. Never invoke this worker as a substitute for them.

## Revalidation and forward intent

`ForwardUpdateInputs` independently authenticates the retained exact manifest
and signature against pinned public keys, compares their hashes and target
identity with Prepared, streams target and every original baseline hash, and
checks both target/original PE identities and current app/settings state. It
rejects unexpected operation/app entries and retained scratch/evidence from
another attempt. It does not require the download stage to still exist: the
prepared operation owns independently authenticated target/metadata copies.

The fixed-repository read-only discovery client must again return Available
with the exact same signed bytes; deleted/draft/changed releases, unavailable
checks, API backoff and incompatible contexts fail closed. Actual OS/x64,
installed version, schema/channel/protocol and the same 24-hour UTC/monotonic
freshness bounds are rechecked. There is no local timestamp bypass. Stream pins
protect retained target/metadata/baselines through the exchange; live settings
are compared again, not held locked while a replacement may migrate them.

After the exact old process has exited, `UpdateTransaction.ApplyAsync`:

1. Yields off the caller's dispatcher and bounds preflight by ten minutes.
   Checks the post-preparation disk reserve from `PreparationBudget`.
2. Creates/flushed-verifies `forward-app.tmp` inside that operation, retaining
   `target-app.exe` and `original-app.exe`. A pre-existing scratch/displaced
   file is evidence, never overwritten. Rechecks scratch PE/hash, baseline,
   space, freshness, exit proof and cancellation before arming.
3. Persists Handoff then Replacing, rechecks baseline/freshness and performs a
   same-volume `File.Replace` of **only** `app/PrivacyMask.App.exe`. The displaced
   old file goes to `displaced-original-app.exe`; the original recovery baseline
   is never consumed. Metadata/ACL errors are not ignored. Verifies both sides,
   then persists AwaitingReadiness. There is no recursive root/data swap.
4. Pins the installed target and starts that fixed executable, shell execution
   disabled, from the install root. Arguments are only `--update-start`, random
   pipe, validated install/operation IDs, worker PID/creation time and bounded
   `normal`/`minimized` preference; no arbitrary image, URL, script, PIN or key.

Cancellation is honored before handoff. Once armed, a competing user cancellation
cannot abandon a half-applied installation; internal startup/cleanup deadlines
remain. The same worker instance's single-flight gate covers failure recovery
too. No general forward-resume API exists: an interrupted uncommitted operation
must be rolled back by the stable launcher, not reissued as a new forward apply.

## Ready, durable commit and receipt

The existing restricted first-instance/current-user pipe validates actual peer
PID, retained creation time and image **before** disclosing a fresh challenge,
and again on replies. Fixed 70-byte protocol-1 frames bind IDs, role and nonce;
the worker races readiness against exact-child exit, with a default 60-second
deadline. It rejects malformed/wrong operation/nonce/peer, disconnect, early exit
and stalls. The 100 ms alive observation after Ready is a race check, **not** a
claim that startup succeeded just because a fixed delay elapsed.

The actual App must later send Ready only after validated settings migration,
singleton, shell/tray and privacy initialization. The test peer's response does
not prove any of those real-App requirements. This remains an activation gate.

After authenticated Ready and target revalidation, the worker durably promotes
Committed **before** sending Accepted. The new final `Receipt` frame (role 4)
confirms the child received that commit result, with the same IDs/nonce and peer
checks. Premature/reused confirmation is refused. Existing prototype peers still
use their original Accepted-only fixture path; they do not authorize commits.

If commit promotion succeeds but later callback/IPC/receipt fails, the worker
returns `CommittedReceiptLost` and **never** kills/rolls back a committed child or
deletes its backups. It tracks promotion inside the journal callback, not just
after `Write` returns, so an exception immediately after promotion cannot undo
success. A normal `Committed` result requires receipt confirmation. Commit is
startup transaction state, not a long-term stability or gapless-mask guarantee.

## Failure and outcome boundaries

Before handoff, `Rejected` leaves the installed app/settings/journal unchanged;
a newly copied scratch may remain as evidence. It does not undo an external
settings edit or resume an old process. After handoff, the worker cancels and
observes pipe/exit waits, stops **only the retained child it created**, and waits
up to five seconds for confirmed exit before releasing pins and calling the
existing recovery kernel. It never force-stops the old process or another app.

If exact replacement exit cannot be confirmed, no settings/payload restoration
is attempted: return RepairRequired and retain journal/backups. Otherwise known
app+settings are recovered together; locked/unknown/corrupt files preserve
evidence and yield RepairRequired. A retry after unlocking can resume rollback.
`RecoveredFiles` means **disk baseline restored, old app not restarted**. The
production stable launcher must restart/qualify that old app and communicate the
actual recovery outcome; that is still required before UI activation. Reasons
are bounded codes without paths, URLs, nonce, raw exceptions or settings data.

## Test evidence and resource scope

Run separately (PowerShell 7 and .NET 10 SDK for developers only):

```powershell
pwsh -File scripts/test-update-transaction.ps1
```

It publishes a framework-dependent single-file console TEST peer twice
(`0.0.0`, `0.0.1`) with app-compatible version resources, not the privacy runtime.
These developer-only peers reuse the already-required .NET 10 runtime rather
than copying a full runtime through every fault case. This is not an end-user
packaging/footprint measurement: the real App release remains self-contained,
and its existing full signed real-App staging/preparation suite is unchanged.
Only fixture versions
are overridden; repository/app versions are unchanged. An in-memory ephemeral
ECDSA key signs a streamed test ZIP; public trust, fake HTTP discovery, real
stager and normal preparer/worker constructors validate the files. Only startup
deadlines/checkpoints/free-space readings have explicit test fault hooks; there
is **no process/exit/PE/signature bypass** in the process suite. No private key
is persisted, external network contacted, registry/startup/shortcut changed or
real App process executed. Test peers have no masks, real settings migration or
normal launch, and reject other arguments.

32 process cases cover normal/minimized success, live/timeout/unclean old exit,
deleted release, disk preflight, changed target/settings and evidence collision,
cancel before/after arming, locked replacement/recovery, early exit/disconnect/
wrong nonce/operation/stall, synthetic failed migration with absent settings,
lost/malformed/missing receipt, IO after swap and logical interruption at handoff,
replacement, readiness and commit temp-flush/promotion boundaries. Assertions
verify exact old/new hashes and settings/absence, retained baseline, durable
commit before receipt, committed-child survival and safe retry of locked recovery.

Each case releases its exact fixture processes/handles before deleting only its
validated owned Temp child, keeping peak fixture disk use to one case rather
than retaining all 32. Owned Temp cleanup revalidates its absolute boundary and
reparse policy and retries transient image/AV locks for at most ten seconds,
without changing ACLs/attributes or skipping persistent failures. This test-only
retry is not a production rollback/cleanup policy. Bundle extraction is scoped to that run's private cache
and removed afterwards. Archives/copies are streamed, not app-sized byte arrays.
Published fixture output is ignored under `artifacts/update-transaction` and is
never part of the normal release ZIP. Test build outputs use an isolated
artifacts path instead of rewriting a running standard-bin host. Do not run simultaneous publishes to the
same fixture output directories.

The source unit suite adds 28 revalidation/identity cases (568 total) and extends
receipt single-use tests. CI adds an independent Windows transaction job, retaining
the full unit/installer/GUI/real-App signed-package and SingleFile/NativeAOT probe
jobs. NativeAOT probe results still do not measure the fully linked production
worker; final packaging/resource measurement remains necessary.

These are logical fault points with controlled cleanup, **not real process-kill,
power-loss or disk-exhaustion qualification**. Real Windows 10/11 standard-user
signed App A-to-B, long-run resources and privacy/PIN/hotkey/DPI matrices remain.

Next: real App authenticated shutdown/start/readiness and settings-writer/migration
contract; a stable launcher with no-journal/boot recovery and old-version restart;
coherent first installer/shortcut/startup/uninstaller migration; safe bounded
retention. Then Settings/Tray integration, consent/Panic/edit gates, preferences/
network disclosures and version bump, followed by authorized release qualification.

References: [File.Replace constraints](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-10.0),
[awaited process exit](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexitasync?view=net-10.0),
[single-file deployment models](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview),
[preparation](update-preparation.md), [recovery model](update-recovery.md).
