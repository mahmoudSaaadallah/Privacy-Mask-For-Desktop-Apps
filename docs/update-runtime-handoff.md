# Runtime update handoff (phase 5d.5)

Status: source delivery `2.0.0-beta.6`, FileVersion `2.0.0.6`, AssemblyVersion
`2.0.0.0`, settings schema 12. App and launcher adapters now use the
[authenticated handoff](update-handoff.md). Shared compiled trust roots remain
empty, so apply fails closed before network, preparation, pipe connection or
shutdown. No Settings/tray update action, tag, release or user installation is
created by this delivery.

## App gates

The internal `ShellCoordinator.ApplyStagedUpdateAsync` entry takes an already
verified staged operation and channel. No CLI, timer, singleton activation or
ordinary Exit invokes it. The later update UI will call this entry.

`UpdateRestartCoordinator` runs on the dispatcher and admits one restart. It:

1. rejects shutdown or any global/profile Panic, including profiles absent
   from current window discovery;
2. waits for complete editor Save/Panic event handlers and resolves pending
   edits through Save/Discard/Cancel; invalid numeric/grid input remains in
   the editor and blocks restart;
3. obtains explicit consent explaining that masks disappear during restart;
4. suspends new shell writers and UI mutations, leaving overlays/refresh alive,
   then drains accepted operations and their UI continuations;
5. rechecks shutdown, Panic and unsaved/binding-error state after draining and
   immediately before authenticated acceptance;
6. prepares under the managed-install lease, transfers the lease to its exact
   installed launcher, completes the pipe exchange, then awaits the existing
   application shutdown/disposal/singleton-release sequence.

The App qualifies its default LocalAppData installation and settings path.
Source/portable/custom roots are not automatic-update targets. A launcher pin
and retained process identity cover spawn through handoff; the worker image
is never selected by PATH or shell execution. Both peers bind install ID,
operation ID, PID, creation time and fixed executable path.

The sixth fixed frame, `ReadyToExit`, acknowledges the client's final live-App
identity check. The App cannot exit between `Complete` and that check. Once
the worker has acknowledged readiness to await process exit, user cancellation
cannot resume writes or interrupt completed handoff.

## Cancellation and failed preflight

Before completed handoff, cancellation or worker failure keeps the App alive.
The App closes its endpoint, confirms its own retained worker stopped, acquires
the lease and retires only the exact still-Prepared operation. Abandonment
checks current and retained app/settings snapshots, durably records recovery
and archives the journal without restoring or changing installed files. Only
then may writer admission and the editor resume.

If cleanup, baseline verification, worker exit or journal retirement cannot be
confirmed, writers remain closed and masks/resources remain alive. The tray
offers ordinary Exit with a close-sensitive-windows/restart instruction; next
managed startup uses normal recovery. Unresolved state is never silently
thawed. Partial operation/staging evidence is retained; bounded pruning remains
a gate before end-user activation.

## Installed launcher

`--update-apply` routes through strict parsing, including fail-closed rejection
of case variants. Apply requires configured compiled trust, the default exact
launcher path, private managed layout, exclusive lease and exact Prepared
journal. CLI/environment/downloads cannot supply keys or URLs.

The worker retains the exact old App with the requested creation time,
authenticates the pipe server, re-verifies signed retained inputs, fresh release
selection, current baselines and available space before requesting exit. It
then requires exit code zero from that retained handle before `UpdateTransaction`.
It never kills the old App. User cancellation ends at handoff; existing internal
transaction deadlines/recovery rules continue to apply.

A committed or durable-commit/receipt-lost outcome keeps the new child. A
post-exit rejection or recovered failure restores/archives under the same lease,
validates and starts the previous exact application with the requested minimized
mode. A failed restart or incomplete restoration reports repair. Pre-exit errors
return to the live App without blocking its worker behind a modal message.

Ordinary launcher boot refuses uncommitted recovery while the exact managed
application is still running. This protects the lease-transfer interval from
another shortcut launch restoring settings under the live App. Inability to
identify a potentially relevant process fails closed.

## Evidence and remaining work

Unit tests cover suspension ownership, safe cancellation, single-flight restart,
late Panic/edits, writer/UI drain ordering, failed-cleanup refusal, unapplied
journal retirement, boot refusal and unconfigured/malformed apply with no HTTP.
Compiled WPF smoke checks exercise Save finally/close/discard behavior during
update suspension and cancellation/shutdown ordering.

The transaction smoke matrix has 38 cases, including six launcher cases using
signed test executable peers: normal/minimized commit, deleted-release and
changed-process-time rejection before exit, rollback/restart and refusal of
ordinary boot while the old App is alive. `-LauncherOnly` selects those six
cases during development; CI runs the complete matrix. Fixture trust and test
root/restart hooks are internal injection seams, never shipped CLI options.
The bootstrap matrix still qualifies SingleFile and Native AOT IPC.

These are synthetic peers, not live real-App A-to-B qualification. Next:
bounded safe retention, discovery preferences and Settings/tray UI/privacy
wording, production trust/signing approval, then signed real-App A-to-B and
Windows 10/11 standard-user/fault qualification. Network activation remains
disabled until its product and release gates are satisfied. The subsequent
[beta.7 storage maintenance delivery](update-retention.md) implements bounded
retention and admission while preserving unknown/uncommitted evidence.
