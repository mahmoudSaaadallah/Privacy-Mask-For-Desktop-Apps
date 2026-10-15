# Awaited shell shutdown foundation (phase 5d.1)

Historical slice note: this describes the beta.3 boundary. The subsequent
[authenticated App readiness delivery](update-app-readiness.md) implements the
strict startup/migration half and advances current source to beta.4.

Status: **shipped App lifecycle changes, not updater activation**. Source version
advances to `2.0.0-beta.3`, Windows FileVersion `2.0.0.3`; AssemblyVersion remains
`2.0.0.0` and settings schema remains 12. No release/tag, production key, installed
files, startup/shortcut target or update/network preference is changed by this PR.
App/Core/Windows still do not reference Updates; there is no Update button or
authenticated `--update-start` / shutdown IPC path yet.

## Complete mutation serialization

`ShellOperationQueue` wraps each complete startup/settings/blur/enrollment/PIN/
startup-preference operation, including its in-memory baseline and UI updates
after awaited IO. A file-level semaphore alone cannot protect a model cloned
before a competing save, so callbacks clone the latest baseline **when they run**.
This queue is in-process coordination, not a managed-install lock or cross-process
filesystem authority. `JsonSettingsStore` and its normal load/recovery semantics
are unchanged; strict transaction migration still needs a separate path.

Callbacks run FIFO on their admitting caller's synchronization context, never
through `Task.Run` or `.Wait()` on the dispatcher. At most 32 running/queued
callbacks are accepted, bounding key-repeat accumulation; overflow reports a
retry hint without changing another app. Panic and enrollment retain their own
single-flight gates from admission until completion, preventing repeats from
queuing a second toggle while a dialog/save waits. These are not priority panic
queues: a panic request can wait behind an already accepted settings operation.

Blur and panic shortcuts capture the target profile before queuing. Enrollment
captures the window snapshot and rechecks that its handle still identifies a
window with the same process name before using the normal eligibility policy.
Focus changes cannot redirect a queued request to the newly focused application.
Deleted/disabled profile targets are rechecked against current settings.

Whole-editor saves and tray startup changes require an idle queue. A prebuilt
editor snapshot is not silently queued behind a mutation and used to overwrite
newer saved preferences. Busy Save reports a retry message and keeps the edit
session; review the visible values and try Save again. A busy startup click
restores its menu check to the saved choice. Repeated blur adjustments may queue;
no arbitrary cancellation interrupts an already accepted file write.

Operation errors belong to the original caller; they do not poison later
callbacks or the shutdown drain. Existing persistence errors retain their UI
feedback; otherwise hotkey/tray tasks have a bounded generic error observer.
No raw PIN/settings/exception data is added to logs or network traffic.

## Normal exit sequence

1. `ApplicationShutdown` publishes one completion before invoking callbacks, so
   duplicate/reentrant exit requests join the same sequence.
2. The coordinator closes admission to mutations and freezes new settings,
   previews, discard, external activation and hotkey/tray input. The dispatcher,
   overlay refresh and resources remain alive while accepted work drains.
3. The window waits for its entire Save and global-Panic event handlers, including
   their UI `finally` continuations, not only the underlying persistence callback.
   Closing/Esc/Discard cannot replace a saving view model. A closing window is
   never re-enabled by a late save continuation.
4. After both drains, dispose hotkeys, overlays, tray/icon and the settings window.
   Only then release/dispose the normal singleton and call WPF `Shutdown`.

`OnExit` is now synchronous last-chance resource cleanup. It is not an `async
void` acknowledgement. OS sign-out, process termination, power loss, unhandled
failures and the existing force-stopping installer **do not** gain a graceful
exit guarantee. Pending modal dialogs require normal user resolution; this PR
does not dismiss a PIN dialog or invent a PIN result to force exit. There is no
timeout that silently force-kills the current application. A failed drain keeps
the normal instance held, reports failure and does not claim clean exit. If
forced termination is necessary, close sensitive windows first.

The normal Exit command retains its existing semantics: it can close an app
with transient panic state, and unsaved edits are not auto-saved. **Do not use
this command as update authorization.** A future update gate must first resolve
Save/Discard/Cancel, wait pending UI/writes, reject any global/profile panic,
explain the mask restart gap and obtain consent; then recheck under its own
handoff/managed-install authority. A permanent stopped queue cannot be reopened
as a canceled pre-consent update. The worker must still authenticate shutdown
IPC and observe the exact native old-process exit before replacing anything.

## Evidence and remaining gates

19 new unit cases cover FIFO/post-write ordering, admission/stop races, capacity,
busy snapshots, synchronous/asynchronous/canceled callbacks, reentrant/failed
shutdown, singleton ordering and real JSON writes/locked-file failure in owned
Temp fixtures. Persistence checks retain the PIN verifier, custom profile/path,
hover dimensions, shortcut state, onboarding and disabled startup preferences.

The existing compiled WPF runner adds actual asynchronous Save/Panic handler and
dispatcher tests: close/discard during Save, rejection after shutdown starts,
UI continuations before closure, disabled closing controls, and queue/disposal/
singleton/exit callback order. Additional cases prevent a late Save continuation
from marking post-submit edits or a replacement session's edits as persisted.
It uses a separate test Application and synthetic
profiles, not the production shell, real registry, third-party capture or an
installed app. These tests do not qualify actual native singleton contention,
normal tray exit with live masks, per-monitor input or end-to-end updates.

Run `scripts/verify.ps1` for unit/solution/installer/compiled GUI checks. The signed
transaction and full real-App package suites remain separate CI jobs/steps and
must pass after the version change. No performance reduction is claimed from
these tests; this adds bounded small operation bookkeeping, not a second service,
full-runtime helper, timer or app-sized memory buffer.

Next: strict prepared-baseline settings loading/migration and private settings
writer, real App authenticated startup/readiness/receipt and shutdown IPC, stable
launcher boot recovery/old-version restart, first-install/shortcut/uninstaller
migration and bounded retention. Then consent/Panic/edit/network/UI integration
and authorized signed A-to-B / crash / Windows 10/11 non-admin qualification.

See [delivery plan](in-app-updates.md) and [transaction worker](update-transaction.md).
