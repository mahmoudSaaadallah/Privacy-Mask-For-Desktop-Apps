# Bounded update storage maintenance (phase 5d.6)

Source delivery: `2.0.0-beta.7`, FileVersion `2.0.0.7`, AssemblyVersion
`2.0.0.0`, settings schema 12. This extends the [runtime handoff](update-runtime-handoff.md)
with local storage maintenance. Production trust remains empty; no update UI,
network preference, tag or release is activated.

## Authority and protected data

`UpdateRetention` requires the caller's held, validated `ManagedInstallLease`.
The caller must not share that lease between concurrent maintenance/mutation
operations. Maintenance only enumerates the fixed direct descendants
`update-operations`, `update-staging` and `update-pruning`. It never traverses
other root files, changes the installed App/settings, weakens ownership/ACLs,
follows reparse points, executes payloads or makes network requests.

Any active journal, including a terminal-but-not-archived record, defers all
cleanup. Invalid or unreadable journal bytes preserve storage as well. Every
directory and file must remain private and non-reparse, with canonical operation
IDs, recognized fixed filenames and bounded sizes. Unknown files, subdirectories,
foreign access, locks and conflicting destinations preserve evidence.

## Retained recovery pair

Archived terminal records must match the installation and directory ID, pass
schema checks and be Committed or Recovered. The keeper is a Committed record
whose target snapshot matches the installed executable; target SemVer, archive
time and canonical ID provide deterministic ordering between matches. Before
deleting older history, the keeper's original executable and both independently
present/absent settings snapshots must match their hashes. Files are streamed
and pinned through maintenance, including the installed executable.

A matching Recovered record is the fallback when no valid matching Committed
pair exists. With no proven pair, all terminal histories and operation
quarantines remain intact. Malformed or unknown histories remain separately
preserved. This can leave more than one directory and consumes admission
capacity; cleanup never invents a replacement backup to meet a numeric target.

Unarchived operation directories are unresolved, even if old. Their baseline
copies never expire automatically. Age is not evidence that an uncommitted
backup is disposable. Repeated unresolved attempts eventually block new
downloads/preparation, while normal verified startup remains available.

## Staging lifetime and activity

New workspaces hold an exclusive, empty `.stage.lock` across the entire download,
verification and extraction, including gaps between writes. Completion removes
the marker and returns the original exact four-file stage contract. Failure
closes the handle before path-scoped owned-file cleanup. A process crash leaves
an unlocked marker that maintenance can inspect later.

A stage is eligible only when directory creation/write times and every file's
creation/write times are at least 24 hours old. Fresh/future times protect the
stage. Maintenance admits only a complete four-file legacy stage, an empty
directory, or a recognized partial stage with the activity marker. Legacy
partial stages without a marker remain evidence. All files must be exclusively
openable before quarantine; a live marker prevents cleanup even if the cleanup
clock is advanced. The operation selected for apply is explicitly protected.

`UpdateStager.PrepareManagedAsync` is the managed runtime adapter: the caller
retains the install lease, maintenance runs before capacity admission, then
download/staging uses the existing anonymous verified pipeline. Plain
`PrepareAsync` remains the isolated low-level staging API; future runtime UI
must use the managed adapter and must not dispose/share its lease mid-operation.

## Interrupted cleanup

Eligible directories are atomically renamed, without overwrite, into the same
volume's private `update-pruning` directory:

```text
update-operations/operation-<id> -> update-pruning/operation-<id>
update-staging/operation-<id>    -> update-pruning/stage-<id>
```

This namespace is the durable deletion intent. Only allowlisted direct files
are removed, one by one, after rechecking the lease, journal, ownership and
paths. There is no recursive deletion. The terminal journal is deleted last;
an interruption leaves a recognizable record or an empty directory. A later
pass resumes quarantine only after the relevant protections still hold. Unknown
new files stop cleanup and remain untouched. Cleanup never restores files or
interprets archived/unpromoted metadata as an apply command.

## Bounds and retry behavior

| Bound | Policy |
| --- | --- |
| Catalog scan | At most 64 direct entries per area, before mutation |
| Per-directory scan | At most 32 recognized direct files |
| Deletion work | At most 4 fully removed directories per run |
| New-operation admission | Refuse at 8 operation entries or 8 quarantine entries |
| New-stage admission | Additionally refuse at 4 staging entries |
| Maintenance IO deadline | 15 seconds, checked between operations and during streamed hashing |
| Individual files | Existing 512 MiB App/archive, 16 MiB settings and small metadata limits |

Unknown files count toward capacity. These bounds control work and new updater
allocations; they do not erase foreign data or promise a fixed total disk size
in a pre-existing damaged layout. A full/ambiguous store needs inspection or
manual repair, not repeated automatic attempts. OS filesystem calls can exceed
a cancellation deadline before returning. There is no recurring cleaner,
background timer, additional service or whole-executable byte-array allocation.

Repeated locked-file rollback uses one deterministic `restore-<label>.tmp`
per app/primary/secondary component, reusing it only after checking its exact
baseline hash. Conflicting/partial scratch remains evidence and requires repair.
Repeated journal writes use one `.update-journal.tmp` and at most one bounded
`unpromoted-journal.json` evidence copy per operation. Byte-identical interrupted
metadata is deduplicated; conflicting evidence is retained and stops the write.
Only the promoted active journal grants recovery state. Old GUID-named root
journal temporaries remain untouched; they are not searched as commands.

## Runtime integration and evidence

Normal launcher boot runs best-effort maintenance after recovery/terminal archival
and before verified App startup; maintenance failure does not block a healthy
App. Apply rollback runs it after archival and before restarting the previous
App. App preparation/cancellation run it while writers remain frozen, protecting
the selected stage. `RecoveryPreparer` enforces capacity before new copies or
fresh discovery. The committed readiness exchange keeps its active journal and
is not disturbed by cleanup; a later safe boundary archives it.

Tests cover terminal selection, absent/empty settings, active/corrupt journals,
orphan backups, stage age/activity, future clocks, file/scan/admission limits,
locks, ACLs, junctions, quarantine interruption and changed/unknown evidence.
Repeated recovery/journal faults verify bounded scratch and correct rollback.
The signed-package smoke host additionally downloads a real signed App into
managed staging with offline HTTP, prepares/archives it, prunes a duplicate
obsolete real-EXE baseline and verifies the retained pair and unchanged live
fixture App/settings. No installed user App is executed or cleaned by tests.

Next: Settings/tray update UI, preferences/privacy wording, approved compiled
trust/signing setup, and real signed App A-to-B/Windows 10/11 standard-user fault
qualification. Physical disk-full, power-loss and measured live performance
qualification remain release gates.
