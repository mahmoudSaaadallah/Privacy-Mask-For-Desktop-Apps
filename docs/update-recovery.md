# Update recovery protocol and threat model

Status: implementation requirements and pending qualification, not an existing
rollback or power-loss guarantee. The installed app and current installer are
unchanged. See [delivery phases](in-app-updates.md) and the
[signed payload contract](update-manifest.md).

The internal [phase-5a recovery kernel](update-recovery-kernel.md) now implements
private layout/lease validation, atomic journal writes and fixture-tested
rollback primitives. It is not connected to a launcher/app/installer, does not
execute processes and does not complete the runtime guarantees below.

## Invariants

1. No application mutation before signed download, verified extraction,
   compatibility checks and explicit apply consent finish.
2. A failed pre-handoff operation leaves the running app, settings, shortcuts
   and startup registration untouched.
3. Data and bootstrap/recovery entry points are outside the replaceable app
   directory. Never replace the entire settings-containing install root.
4. One install-scoped lock serializes launcher, worker, installer, uninstall
   and competing apply attempts. The normal WPF singleton is a distinct lock;
   the worker does not acquire it as though it were the application.
5. Before every irreversible step, persist its intent and enough bounded
   recovery metadata. Preserve a known working backup until commit is durable.
6. Recovery is confined to the canonical owned installation and the exact
   operation. A corrupt journal is not a command file and does not authorize
   deletion, execution, elevation or arbitrary path changes.
7. Starting a process, displaying a tray icon, or waiting a fixed delay is not
   readiness. Success requires an authenticated operation-specific result.
8. No panic state/PIN bypass, discarded edits without a decision, silent
   settings reset, or captured-frame persistence/transmission.

## Operation protocol

| State | Action and failure behavior |
| --- | --- |
| Discovered / downloading | Keep masks active. Cancel/network failure removes only this operation's partial files. |
| Verified | Recheck signed bytes, package/files, version, OS, protocol, install identity and disk space. Expired discovery requires a new check. |
| Prepared | Acquire install lock; freeze selected target; resolve edits; await settings writes; recheck all panic states and consent. Backup saved settings and record handoff intent. |
| Handoff | Worker signals ready. App stops accepting competing settings/apply operations, awaits shell disposal, releases singleton and exits. On timeout abort; do not force-kill by default. |
| Replacing | Worker confirms exact old PID/creation time/path exited, revalidates staged payload, journals each file move, preserves prior app and installs new app on the same volume. |
| Awaiting readiness | Launcher starts the exact replacement, with a fresh operation handshake and user's minimized preference. Detect startup failure, exit and deadline. |
| Committed | Persist verified readiness and commit before releasing backups. Bounded cleanup can be retried later without undoing a committed app. |
| Rolling back / recovered | Stop only the exact replacement if required, restore previous app/settings coherently, persist recovery state, restart previous app, then report the actual result. |
| Recovery required | If rollback itself fails, preserve backup/journal, release resources safely and show manual repair instructions. Never relabel this as success. |

Serialize final consent, panic recheck and handoff arming with shell commands.
A panic activation before shutdown is armed cancels the pending apply. Do not
clear panic, synthesize an unlock, or change its persistence model to allow an
update. Any global or per-profile panic must be unlocked through the existing
verified PIN workflow. A cancel/wrong PIN leaves both mask and app version alone.

At a busy-state or shutdown timeout, leave the installed files unchanged and
explain how to retry. Do not infer process identity from executable name alone,
and never stop a portable/source process or another user's instance by name.

## Journal and durable restart recovery

Create the operation directory with a fresh random identifier and current-user
access controls. Store a versioned journal using bounded JSON, explicit state,
install identity, selected version, signed metadata hashes, fixed relative
staging/backup names, completed steps and settings backup presence. Store no PIN
digits, captured frames, secrets, arbitrary command line, executable command
text, or unvalidated absolute targets. The journal is local sensitive metadata,
not something uploaded in diagnostics.

Use same-volume staging and renames, write-through journal files, a flushed
temporary journal then atomic replacement, and write-ahead step intent. Each
step must be idempotent when interrupted before/after its durable marker.
Interpret physical file state and verified hashes alongside the journal,
because an intent marker alone cannot prove a move completed.

The stable launcher runs before the replaceable app on shortcut/sign-in
activation. It acquires the install lock and examines pending work. An operation
without durable commit is restored to the prior app/settings before normal
launch; if readiness happened but commit was lost, prefer consistent recovery
over guessing success. A committed operation only needs safe cleanup. Corrupt,
unknown-version or conflicting records preserve evidence and require repair;
they cannot drive arbitrary filesystem changes.

Require a journal schema version and installation ownership checks before any
cleanup. Re-resolve roots and reject reparse points before use. No recursive
delete at user profile, LocalAppData, Temp, drive root, repository root, or an
unresolved path. Retention cannot evict a pending operation's last viable backup.

The launcher must remain usable if the `app` directory is absent between moves.
Uninstall and manual repair must participate in the same protocol. A background
worker alone cannot recover after power loss because that process is gone;
shortcuts and startup must reach the stable launcher, not the missing app.
The bootstrap executable is not replaceable by a v1 application manifest.

Flushes and journaling cannot guarantee recovery from broken hardware, corrupt
volumes, hostile antivirus deletion of every copy, or manual removal of both
app and backup. Qualify supported crash points on Windows and document limits
instead of claiming that every power-loss scenario is solved.

## Settings migration and rollback

- Preserve the existing settings data path; don't load defaults from a new
  empty app directory. Do not ship a settings file in an update package.
- Finish a user's Save/Discard choice before backing up the saved baseline;
  if Save fails, abort handoff. Freeze settings writers during snapshot/apply.
- Preserve existence/absence and exact bytes of primary JSON and `.bak` plus
  existing corrupt-file evidence. Do not confuse an update snapshot with the
  ordinary one-save `.bak` file or delete pre-existing evidence during cleanup.
- In update-start mode, validate/migrate from the snapshot to a new local
  staged settings file before promoting it atomically. A parse/migration error
  fails readiness: never silently start with defaults and call the update good.
- The snapshot includes the salted PIN verifier only as part of private local
  settings. Do not copy it into the manifest, journal, logs or network requests.
- Rollback restores the pre-migration primary/backup existence and bytes along
  with the matching old app. Preserve failed-new-version diagnostics locally
  without replacing the only viable old configuration.
- Validate profile IDs, names/targets, appearance, hover, shortcuts, startup
  and PIN verification behavior after both upgrade and rollback. No update
  counter changes the application's independent settings schema implicitly.

## Readiness and IPC

Use a current-user-restricted local IPC channel, such as a named pipe, bound
to install ID, operation ID, fresh nonce and the launched child process. Check
the peer's PID/creation time, expected canonical path and verified executable;
reject wrong IDs, stale acknowledgements, unsolicited messages and replay.
No fixed global event or journal field alone may acknowledge success.

The replacement reports ready only after:

- Acquiring the normal singleton without reactivating the old process.
- Loading and validating the intended settings, finishing an allowed migration,
  and preserving the agreed preferences/credentials.
- Starting tray, shell and protection initialization and finishing an initial
  inspection/refresh where applicable. Empty candidate lists are not errors.
- Completing hotkey registration attempts; an ordinary reserved shortcut is
  reported normally, not treated as a fatal startup failure causing rollback.
- Surviving the bounded startup observation without a fatal startup error and
  returning the matching handshake. Record a default 60-second readiness
  deadline for qualification; adjust only with measured slow-machine evidence.

These checks show startup readiness, not an eight-hour stability guarantee or
proof that every external application's capture mode respects overlays.
On success, send a commit confirmation so the new UI does not display success
before the worker has recorded it. If acknowledgement/confirmation times out,
leave a consistent pending operation for recovery rather than delete backup.

Both worker and replacement recognize update-start arguments before normal
activation. Arguments contain only validated operation identifiers; they must
not expose PINs or allow an attacker-selected executable to run. Native error
reporting must remain available if the WPF app cannot start. The helper should
not flash a console window or require an interactive administrator session.

## Threat boundaries

| Threat / failure | Required mitigation |
| --- | --- |
| Modified download or substituted hash file | Pinned manifest signature plus signed archive/file hashes; no unsigned fallback |
| Compromised GitHub metadata/redirect | Fixed repository and HTTPS host policy; signed identities; strict version and asset matching |
| Malicious ZIP paths, links or compression bomb | Exact single-file v1 allowlist, quotas, reject links/reparse/duplicate entries, isolated staging |
| Payload replaced between verify/apply | Worker independently rechecks trusted bytes and hashes; protected staging and safe filesystem operations |
| Equal/older version or mixed release assets | SemVer policy and operation-pinned signed metadata |
| Stale/spoofed readiness or process ID reuse | Restricted IPC, nonce, operation binding and child creation-time/path checks |
| Concurrent install, uninstall, update or sign-in | Shared install lock and persistent pending-operation recovery |
| Power interruption, disk-full, locked files | Write-ahead recovery, stable launcher, same-volume staging, kept backups and idempotent rollback |
| Restart clearing runtime panic | Refuse apply until normal authenticated unlock; recheck before handoff |
| False privacy expectations | Warn about restart mask gap; update network disclosure; no content telemetry |
| Trusted private key/maintainer/CI compromise | Protected signing/release approval, key incident/bridge procedure; signature alone is insufficient |
| Malicious process already running as the same user, administrator or OS | Outside full protection scope; these can already replace local app/data or kill masks. Do not advertise the PIN as an OS security boundary. |

## Qualification matrix (all pending in phase 1)

Automated fixtures must exercise each logical state and crash boundary; live
Windows tests are also required. Never run fault injection against a user's
real installation or sensitive applications; use owned disposable fixtures.

| Test | Expected observation |
| --- | --- |
| Managed release A -> B | No SDK/admin; version B, same settings/PIN/startup choice and functioning tray/shortcuts |
| Beta ordering, preview -> stable, stable -> preview | Correct numeric SemVer; no implicit downgrade/channel change |
| Offline/timeout/404/API limit/malformed body | App stays usable; distinct unable-to-check status and bounded retries |
| Download cancel/interruption and oversized HTTP body | Current app unaffected; own partial files cleaned; no unbounded RAM |
| Wrong/unknown signature key, DER instead of P1363, altered bytes/newline | Reject before shutdown |
| Duplicate JSON properties, mismatched tag/asset/channel, missing upload | Reject even if a schema-only test would otherwise accept the object |
| Malicious archive, links, extra file, huge expansion, altered staged EXE | Reject before mutation; no writes outside owned staging |
| Disk-full/access denied/locked file at every move | Keep/restore known app and settings or explicitly report repair-required |
| Pending Save/Discard/Cancel and failed Save | User choice respected; canceled/failed Save never shuts down app |
| Global/profile panic, wrong PIN, panic race before handoff | Apply deferred/canceled; no unlock or content reveal caused by updater |
| Busy/incorrect process or singleton handoff failure | No force kill/name-only targeting; no competing installed app replacement |
| Crash at each journal write/file move/start/ack/commit/cleanup | Next launcher reconstructs consistent state; no premature backup deletion |
| Fatal new startup / malformed migrated settings / lost ready acknowledgement | Matching app + original settings restored; no successful-defaults fallback |
| Spoofed/stale handshake, wrong PID/path/nonce | No commit/backup cleanup |
| Rollback locked/deleted backup and corrupt/unknown journal | Evidence retained; accurate manual repair UI, never claimed recovery |
| Reinstall/uninstall concurrent with apply; first bootstrap fails | Shared serialization and coherent old/new shortcuts/startup/preferences |
| Arabic/spaced paths, custom root, reparse points, source/portable copy | Qualified roots work; unsupported/untrusted paths rejected, source untouched |
| Normal hotkey reservation; minimized/visible user preference | Conflicts remain visible; no false rollback; preference preserved |
| Helper key/protocol incompatibility and installed helper repair | Fail closed with tested bootstrap path; no arbitrary helper download/execution |
| Large package and repeated failure/cleanup | Measured bounded RAM/disk; no persistent service or unbounded cache |

Run the end-to-end path using two authorized published testing releases and a
standard-user account on Windows 10/11, not just a locally copied EXE. Record
each result, machine/OS, version/commit and limitations. Existing live keyboard,
PIN, multi-monitor DPI, resource and stability gates remain prerequisites for
declaring stable 2.0.0; the updater does not waive them.
