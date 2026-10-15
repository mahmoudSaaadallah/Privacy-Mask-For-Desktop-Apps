# Recovery kernel (phase 5a, not runtime activation)

This is the first implementation slice of [phase 5](in-app-updates.md), not a
completed launcher or working end-user update feature. It adds **internal**
Windows rollback primitives and disposable-fixture tests to Updates. App, Core
and Windows still do not reference Updates. There is no process launch/kill,
installer migration, production install mutation, new trust key, release, update
button or scheduled check. Version remains `2.0.0-beta.2`, settings schema 12.

The internal boundary is intentional: these primitives must not become a public
apply API before the helper validates signed content, obtains explicit consent,
freezes settings writers and proves exact process exit. A marker, journal or
Managed enum value alone is not authorization to download, execute or install.

## Implemented layout contract

The caller must supply a dedicated local fixed-drive Windows directory whose
leaf is `PrivacyMask.Desktop`, and an expected nonempty lowercase N-format GUID
install ID. Existing source/portable/legacy flat installs have no supported
marker, so the kernel cannot implicitly convert or update them.

```text
PrivacyMask.Desktop/
  managed-install.json          # schema/app/install/protocol identity
  .update.lock                  # exclusive OS file-sharing lease
  update-journal.json           # only authoritative recovery record
  .update-journal-<guid>.tmp     # interrupted writes: evidence, never commands
  app/PrivacyMask.App.exe       # only replaceable payload, not the whole root
  settings.v1.json              # preserve current data path
  settings.v1.json.bak          # independently present or absent
  update-operations/operation-<guid>/
    original-app.exe            # retained exact baseline
    original-settings.json     # only if originally present
    original-settings.bak      # only if originally present
    failed-app                 # displaced replacement, preserved evidence
    failed-primary
    failed-secondary
    restore-<label>-<guid>.tmp  # only new copies of retained baseline
```

Neither the library nor the production installer creates this layout yet;
fixture setup is test-only. `app` is a stable directory and v1 recovery swaps
the single file, not a settings-containing directory. A missing app **file** can
be restored; a missing/untrusted app directory currently requires repair.

`ManagedInstallLease` rejects broad/relative/UNC/device/ADS/reparse-point paths,
checks current-user ownership and effective access rules on the managed root,
marker, operation paths and touched files, and requires only the current user's
SID to receive allow rules/full control. Existing default/shared ACLs can fail
this deliberately strict prototype contract; future migration must explicitly
provision and qualify the private layout. A hostile same-user process, admin/OS
or malicious concurrent ACL/path substitution is not an isolation boundary.

New lock/journal/restore files specify the user SID as owner and a protected
user-only DACL at CreateNew time, rather than inheriting an elevated account's
default object owner. Existing files are still validated, never silently taken
over. Fixtures explicitly provision this same ownership on baseline directories
and files, so standard/elevated Windows defaults do not determine test results.

The lease opens `.update.lock` with `FileShare.None`, holds that exact handle
until disposal, and rechecks marker/ACL identity under the lease. No persistent
service or named mutex with session/thread-affinity assumptions. Cooperating
launcher, installer and uninstaller must use the same lease later; the current
legacy scripts do **not**, so this kernel must remain disconnected from them.

## Bounded journal and durability

The strict JSON record is at most 16 KiB, schema 1, with install/operation IDs,
bounded monotonic revision, explicit state, original/target strict SemVer and
SHA-256/size/existence snapshots of app and both settings files. It records
signed-manifest/signature **hashes**, not arbitrary executable paths, commands,
PIN digits, settings contents, credentials, or user-chosen filenames.
The journal is private local metadata, not remotely authenticated release data.

Settings snapshots are opaque files, possibly containing the existing salted
PIN verifier; keep them private and local, never upload or include them in a
release. Hashing/copying does not validate settings semantics or prove a PIN
works. The helper/app migration path must perform those checks separately.

Allowed forward transitions:

```text
Prepared -> Handoff -> Replacing -> AwaitingReadiness -> Committed
     any uncommitted state -> RollingBack -> Recovered
```

First record must be Prepared/revision 1. Subsequent writes cannot change the
operation, versions or any snapshot/hash, skip revisions/states, downgrade a
terminal state or replace another operation. Terminal record archival and a new
operation lifecycle API are still pending, not an excuse to overwrite a record.

Write a fresh CreateNew GUID-named temporary, flush the file with WriteThrough
and Flush(true), then atomically promote on the same volume (File.Replace for
an existing active record). No metadata-error bypass or corrupt-record fallback.
Before promotion, a crash leaves the previous active record authoritative; after
promotion, the next record is authoritative. Unpromoted/torn temporaries are
retained but **never** interpreted as committed state. A missing active record
means NoPendingOperation, not proof that the installation is healthy or safe to
launch. A corrupt/foreign/unknown active record means repair; never reconstruct
commands from scratch files or silently use older metadata.

File flush/atomic promotion plus logical fault tests do not prove durable
directory updates under real power loss, failed storage or malicious deletion.
Windows/filesystem power-loss qualification remains required before advertising
automatic recovery guarantees.

## Rollback algorithm and evidence

`RecoveryRestorer` must only run while the exact application has exited and all
settings writers are frozen. It does not enforce process/IPC/consent/Panic itself;
there is no production caller yet. It also does not inspect PE or verify a remote
signature: future preparation must authenticate the target, independently inspect
both executable identities and obtain the original snapshots before Prepared.

1. Under the held lease, read the active journal and validate fixed operation
   paths, app-only directory contents, and **all three** original snapshots before
   touching an installed file. App <=512 MiB; each settings file <=16 MiB. Hash
   with bounded streams; settings can independently be absent or empty.
2. A committed operation is never automatically rolled back. Verify its target
   app still matches; otherwise report repair without guessing from backups.
   A durably Recovered record likewise verifies the original app but does not
   reset legitimate settings edits made after the restored app runs normally.
3. For an uncommitted operation, recognize only original/target/absent app bytes.
   Unknown payload, corrupt/missing backup, oversized/untrusted settings or
   conflicting evidence yields RepairRequired and preserves data.
4. Persist RollingBack intent before mutations. For each differing file, copy
   the authenticated original to a new bounded/flushed scratch, keep the source
   backup, preserve the displaced file at the fixed failed-* evidence name, then
   move the new original copy into place. To restore original absence, preserve
   the current file but leave the destination absent. Never overwrite evidence,
   recursively delete, consume the last good baseline, or touch corrupt-settings
   evidence/other root files. An empty original settings file is restored exactly.
5. Verify each restored file and persist Recovered only when all are consistent.
   A retry uses durable intent plus physical hashes/existence; it can resume after
   preserving/replacing any one file. A committed/recovered result is a disk-state
   result, not proof that a process started, masks work or readiness succeeded.

Locked file, denied access and IO failure return a bounded RepairRequired reason
and leave journal/backups/evidence for a safe retry. A partially restored state
must **not** be launched as though recovery finished. There is no cleanup of
snapshots/scratch/terminal journals yet; evidence retention is deliberate, and
safe bounded pruning remains part of the full helper lifecycle. Copying needs
disk headroom; failure preserves baseline, but full apply/rollback reserve-space
preflight and physical disk-full tests are still pending.

## Verification and next slices

Fixtures create private, owned Temp roots and synthetic executable bytes which
are never executed. Cases cover invalid IDs/versions/snapshots/transitions, strict
JSON corruption/duplicates/unknown fields, crash at journal create/flush/promote,
crash/resume at rollback intent and each file preserve/restore boundary, original
absence/empty settings, known/missing/unknown app, backup corruption, conflicting
evidence, locks at each target, post-terminal settings edits, size caps and
operation junction rejection. A hidden, owned Windows PowerShell child proves
the file lease excludes another OS process and releases on disposal. No user
installation, live application, startup entry, shortcut or registry data is used.

Run `scripts/verify.ps1`; the existing xUnit project includes all new cases.
The existing signed real-EXE staging suite must also keep passing to avoid
regressing phase 4, but it is not real apply/recovery qualification.

Next phase-5 work:

The separate [phase-5b developer prototype](update-bootstrap-prototype.md) now
exercises process-bound readiness and packaging/resource measurements, but does
not activate a production launcher or satisfy the remaining runtime gates.

Remaining steps, in order:

- Prototype the stable launcher/helper and measure published size, RAM/startup
  overhead and Windows baseline compatibility; choose packaging from evidence.
- Connect authenticated preparation/freshness and saved-settings snapshots,
  install identity, same-volume/space checks and terminal-operation lifecycle.
- Add restricted process-bound readiness IPC, exact-process graceful handoff,
  forward replacement, launch, commit acknowledgement and boot recovery.
- Migrate the existing installer/shortcuts/sign-in/uninstaller coherently under
  the same lease, with first-bootstrap rollback and manual repair coverage.
- Then integrate Settings/Tray Update UI, preferences/privacy wording and
  Save/Discard/Panic/restart-consent gates; bump the shipped app version there.
- Qualify real signed release A-to-B and crash/power-loss matrices on Windows
  10/11 standard-user machines before calling phase 5/overall updater complete.

Sources: [File.Replace atomic replacement constraints](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-10.0),
[Windows file-sharing flags](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0).
