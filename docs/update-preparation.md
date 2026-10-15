# Authenticated recovery preparation (phase 5c.1)

Status: **internal library + disposable fixtures**, not a production updater or
apply command. This connects phase-4 staging to phase-5a's journal/baselines.
App/Core/Windows still do not reference Updates. The prototype helper continues
to accept only its test `--probe` command. No user installation, launcher,
shortcuts, startup entry, release, key or network preference is changed.
Product version remains `2.0.0-beta.2`, FileVersion `2.0.0.2`, settings schema 12;
the first shipped runtime integration must follow the beta/FileVersion policy.

## Authority and layout

`RecoveryPreparer` requires an already-held `ManagedInstallLease`. It does not
create/adopt a legacy install, elevate, choose a root, accept executable/URL
arguments, stop/start a process, replace the installed app or migrate settings.
Only a lowercase nonempty N-format GUID operation ID is accepted. It resolves:

```text
PrivacyMask.Desktop/
  update-staging/operation-<id>/    # must already exist, exactly phase-4's four files
    package.zip
    PrivacyMask.App.exe
    update-manifest.json
    update-manifest.sig.json
  update-operations/operation-<id>/ # new only; never reuse an orphan/evidence directory
    original-app.exe
    original-settings.json        # only if primary existed
    original-settings.bak         # only if secondary existed
    target-app.exe
    update-manifest.json
    update-manifest.sig.json
    terminal-journal.json         # only after separate explicit terminal archival
```

All paths are fixed descendants of the same validated, local fixed-drive private
install root. Reparse ancestors, public/foreign ACLs/owner, extra directory
entries, directory-as-file, unknown IDs and an existing active journal are
rejected. The existing `app` directory must contain only `PrivacyMask.App.exe`.
New directories and files explicitly assign current-user ownership and protected
user-only permissions, including phase-4 staged files under elevated Windows
defaults. A malicious same-user/admin/OS process is still outside the isolation
guarantee; the lease serializes cooperating updater components, not the whole OS.

The future coordinator MUST freeze settings writers, finish pending saves and
serialize explicit restart consent, unsaved-edit decisions and every Panic state
through preparation/handoff. This internal API **does not enforce those runtime
gates**. Read-sharing pins present inputs during preparation, not after return;
absent files have no pin, so their absence is rechecked before journaling. A
future helper must independently revalidate all inputs/baselines before handoff
and replacement. `Prepared` is a recovery record, **never apply permission**.

## Verification sequence

1. Check held install identity, marker/protocol, real OS build/x64 process and
   host context; reject source/portable/legacy, incompatible or non-newer targets.
2. Pin both bounded metadata files, authenticate their exact bytes against the
   pinned public keys, then independently stream-hash the staged ZIP and EXE.
   ZIP/file size and SHA-256 must match the signed contract. PE architecture,
   product, numeric version and full version/source SHA must match the manifest.
   No extraction/execution or remote-key fallback is involved at this boundary.
3. Always call the existing anonymous, fixed-repository discovery client again.
   It must return Available with **identical signed manifest and envelope bytes**.
   Offline, deleted/draft/changed release, invalid metadata, API backoff or a
   different highest eligible selection means retry the check, not install the
   old stage. An equivalent manifest re-signed with different bytes is also
   intentionally rejected until staged again. No manually supplied `checkedAt`
   field or local journal can authorize preparation.
4. Measure check age from its start, with both UTC and monotonic elapsed time.
   Negative age/clock rollback or over 24 hours fails closed, including after
   copying. Preparation additionally has a ten-minute IO cancellation deadline;
   the client retains its 45-second discovery bound/backoff. These timestamps
   are in-memory only. A future separate apply process must fresh-check again,
   not treat this journal as a durable freshness receipt.
5. Pin/hash the installed executable and both independently present/absent saved
   settings files. The original PE must match the installed product version and
   contain a valid source SHA; supplied build metadata must agree exactly. Do not
   claim original signature provenance from this identity check: baseline trust
   is the already-qualified managed installation. Settings bytes are opaque,
   limited to 16 MiB each, including exact empty or absent originals. No JSON
   normalization, default settings, PIN plaintext or semantic migration occurs.
6. Reserve disk space; create a new private operation and streamed, flushed,
   CreateNew copies. Keep staged sources and installed originals intact. Recheck
   every retained copy, original settings existence, free space, freshness,
   cancellation and lease identity before atomically publishing Prepared revision
   1 through the existing journal store. The journal records only hashes/sizes,
   versions and IDs; sensitive settings remain in private local backup files.

The private managed layout is a prerequisite for **every** future settings
writer, including a newly created primary/backup under an elevated account.
The synthetic migration fixture explicitly assigns user ownership and asserts
it; using Windows' elevated default owner instead is correctly rejected by the
kernel. This does not qualify the real App settings writer/installer migration
for that layout; that integration must preserve the same ownership contract.

Preparation yields off the calling UI context. Files are hashed/copied as streams,
with a 64 KiB copy buffer and at most the bounded signed metadata in arrays,
not an archive/app-sized allocation. Failures expose bounded reason codes, not
HTTP tokens, local paths, PIN data or inner exceptions. Input handles and the
single-flight gate release on success/cancel/failure; the caller owns the install
lease and client lifetime and must cancel/await before disposing them.

## Space and interruption semantics

Let `B` be original app + both present original settings sizes, and `T` the signed
target EXE size. Existing installed/staged bytes already occupy the disk and are
not counted as available. Before copies require free space of:

```text
2 * (B + T) + 2 * 16 MiB + 64 MiB safety margin + 1 MiB metadata reserve
```

This conservatively includes retained copies, future forward/rollback scratch,
both settings files' bounded growth and journal/metadata overhead. After making
copies, require the remaining `(B + T)` plus the same growth/margins. It is a
current-user free-space preflight, not a filesystem reservation. Another process,
quotas, fragmentation or device failure can still cause IO failure. No actual
full-volume/power-loss qualification is claimed.

Failure/cancel before promotion never modifies installed app/settings and leaves
no active new journal. Partial/new operation files are retained as evidence,
never silently overwritten or executed. Retry must not reuse their ID; orphan
inspection and bounded pruning remain a separate gate before runtime activation.
After promotion the durable Prepared record is authoritative even if a response
or process is lost. Cancellation is deliberately not rechecked after promotion;
the caller must observe the journal rather than assume every cancellation means
no operation. The kernel can recover Prepared from its exact baselines, provided
the real app has exited and all writers are frozen (still caller obligations).

## Terminal journal lifecycle

`RecoveryJournalStore.ArchiveTerminal(expectedOperationId)` is explicit, not part
of preparation. Under the held lease it accepts only that exact Committed or
Recovered record. It verifies the terminal installed app and every retained
original baseline, refuses an existing archive/file/directory collision, then
same-volume-renames the active record to that operation's `terminal-journal.json`.
It never restores terminal settings, changes an uncommitted state, consumes
backups, overwrites history or recursively deletes anything. Another operation
can then start with a new ID/revision 1. Archived records are history only;
the restorer never searches them as fallback commands. A launcher with no active
record must still validate its normal installed app before starting it.

This is lifecycle **archival**, not a completed retention/pruning policy. It
retains all evidence. Keeping only the previous completed app, safely pruning
older operations/orphan staging and bounding repeated failures is still required
before exposing this path to end users. Do not wire automatic retries/updates
while that gate or installer/launcher serialization is missing.

## Evidence and remaining gates

- Unit fixtures use ephemeral in-memory signing keys, fake HTTP and synthetic
  binaries. A private test-only inspection seam skips PE checks in those cases;
  the normal constructor never skips them. Tests exercise exact snapshots and
  kernel recovery, absence/empty pairs, altered metadata/archive/EXE, public ACLs,
  bounds/locks, incompatible context/identity, fresh-selection/expiry failures,
  disk preflight/post-copy shortage, cancellation/single-flight, copy corruption,
  logical interruption before/after journal promotion and terminal archival.
- `scripts/test-signed-update-package.ps1` now also builds a disposable `0.0.0`
  baseline of the **actual App** into its owned Temp fixture without changing
  repository version files. The existing current real signed App payload is
  downloaded/staged through offline HTTP fixtures and prepared with the normal
  constructor (no PE/budget/clock bypass). Both real executable identities,
  retained hashes, unchanged installed-fixture bytes, settings copies and archive
  lifecycle are checked. Neither EXE is executed; no real installation is used.
- Existing helper SingleFile/NativeAOT process tests, all unit/GUI/installer/schema
  checks and signed-package CI must continue to pass. This slice does not remeasure
  the final shipped helper: it is still the test probe, not an apply worker.

The separate [phase-5c.2 transaction worker](update-transaction.md) now implements
internal forward replacement, exact-process exit proof and test-child readiness/
commit/recovery. It does not wire this to a production launcher or the real App.

Next: production helper entrypoints/boot recovery/old-version restart, authenticated
graceful shutdown and real-App readiness/settings integration; bounded retention;
installer/shortcut/startup/uninstaller migration; then UI/consent/Panic/edit gates,
network disclosures/preferences and beta bump. Finally qualify authorized signed
release A-to-B on Windows 10/11 standard-user machines, including actual failure,
disk-full/power-loss and existing protection/performance/stability checks.

References: [read-sharing behavior](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0),
[current-user available free space](https://learn.microsoft.com/en-us/dotnet/api/system.io.driveinfo.availablefreespace?view=net-10.0),
[recovery threat model](update-recovery.md), [bootstrap prototype](update-bootstrap-prototype.md).
