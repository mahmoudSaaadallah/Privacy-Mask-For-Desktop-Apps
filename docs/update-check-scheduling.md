# Durable startup discovery (phase 6b)

Source delivery: `2.0.0-beta.9`, FileVersion `2.0.0.9`, AssemblyVersion
`2.0.0.0`. User settings remain schema 13. The separate check-history format is
schema 1; it is scheduling metadata, never signed release authority.

The subsequent [beta.10 download UI](update-download-ui.md) shares this session,
global retry gate and pending-intent protocol with explicit downloads. A download
does not advance successful discovery timestamps or rearm startup scheduling.

## Activation and resource policy

Production trust remains empty. Neither startup nor manual discovery may touch
history, allocate an HTTP client or start a delay timer before compiled trust and
the exact supported default Windows x64 managed installation pass preflight.
Opt-out also prevents creation of the startup timer and automatic history access;
explicit manual checks remain available under the same trust and retry gates.
No public key, private key, credential, tag, Release or live activation is added.

Once explicitly configured in a later approved build, one 30-second delay starts
after completed onboarding/interactive startup. It is a one-shot, not a recurring
poller or a daily timer. At expiry the saved preference/channel is read again.
An automatic attempt requires at least 24 hours since the last durable attempt,
including failed/offline/cancelled attempts. A backward clock or future timestamp
conservatively postpones it. Long-running apps do not poll repeatedly.

Manual checks may bypass the ordinary 24-hour interval, but never a known GitHub
retry deadline. Retry state is global across Stable/Preview and restarts, not
cleared by switching channels. The existing transport independently retains its
in-memory retry/ETag/response budgets.

Disabling automatic checks and saving cancels the delay or in-flight automatic
request, without cancelling an explicitly requested manual check. Enabling after
startup takes effect on the next start. Changing the saved channel cancels an
already running automatic request for the old channel. Unsaved editor previews
do not change network behavior.

## Durable, bounded control data

Only these fixed direct children of the private managed root are used:

- `update-check-history.json`: at most 4096 bytes, exact schema/field names,
  UTC last-attempt/global-retry/per-channel-success timestamps and request-pending flag;
- `.update-check-history.tmp`: one CreateNew private scratch file, flushed before
  atomic replacement; unknown/crashed residue is preserved, never overwritten;
- `.update-check.lock`: zero-byte private exclusive lease held across read,
  admission, HTTP and result persistence, preventing concurrent Windows sessions
  from overwriting one another's retry state. It is independent of `.update.lock`.

No root directory is created or re-owned by the history store. Parent/file ACLs,
ownership, reparse points, file types and lengths are checked; no symlink/junction
is followed, and paths are never read from the metadata. Duplicate, missing,
unknown, oversized, corrupt, foreign-access or locked input fails closed.
History parsing is bounded to four JSON levels and contains no settings, paths,
profiles, PINs, notes, keys, tokens or captured content.

The pending intent and attempt time are durable **before** HTTP. A completed
response's retry information is saved even if opt-out/shutdown races its return.
A bounded five-second finalization token, independent of UI cancellation,
protects that bookkeeping. Normal cancellation can retire its own intent without
advancing successful-check time; failed persistence preserves the unresolved
intent. An unknown pending request or scratch residue blocks further client
requests rather than silently discarding a possibly lost server deadline.

This conservative failure mode affects update checks only: masking continues,
View releases remains available, and no automatic repair/deletion is attempted.
Do not tell users that a reinstall clears such evidence. Inspect it under user
direction before resetting an unresolved history or pending file; a lost retry
deadline cannot be reconstructed from an unsigned local guess.

The normal installer preserves the bounded non-reparse history byte-for-byte,
including pending evidence, while removing disposable lock/scratch files with
the old installation. An oversized/reparse history aborts before replacement.
In-app App-only replacement naturally leaves root history untouched.

## UI and shutdown

Successful-check timestamps now survive restarts separately by channel; the UI
does not claim they are session-only. They are not a cached UpToDate result:
current availability is asserted only after a completed authenticated check.
Background failures update status without opening a modal or retrying in a loop.
Unknown failures never become UpToDate or expose raw paths/exception strings.

Manual/tray/startup work shares one reentrancy-safe dispatcher-owned session.
Shutdown cancels and awaits both the delay and active check/metadata bookkeeping.
Update suspension cancels/drains the same work before handoff; resume enables
manual checks without rearming a second startup timer. Masks and the settings
edit/PIN session are not changed by discovery or history writes.

## Evidence and remaining gates

Tests exercise durable-before-request admission, failed/offline checks across
restarts, 24-hour boundaries, future clocks, cross-channel retry, opt-out before
and during admission, response/cancellation races, lost-result persistence,
one-shot timer disposal, reentrant requests, suspend/resume, private/bounded
atomic IO, scratch collisions, locks and foreign ACLs. Installer tests preserve
history and reject oversized input without changing the installed fixture.
Tests use disposable roots/delegates/offline HTTP, never an installed user's
check history or a production network/signing key.

Next: staged download/progress/cancel and Update now UI through existing explicit
restart/Panic/edit gates; separately approved signing custody/compiled roots;
real signed App A-to-B, Windows 10/11 standard-user and physical disk-full/
power-loss qualification. This PR does not complete production update activation.
