# Explicit verified download UI (phase 6c)

Source delivery: `2.0.0-beta.10`, FileVersion `2.0.0.10`, AssemblyVersion
`2.0.0.0`. Settings remain schema 13 and check history remains schema 1.
Production compiled trust is still empty. No production network activation,
signing key, tag, Release, installed-App execution or installation change is
introduced. This is the download slice, not completed one-click installation.

## User flow, once approved trust is configured

1. Settings > Updates or the tray checks the saved channel through the existing
   private durable session. An available signed candidate shows bounded plain
   text notes, size and **Download update**.
2. Download requires an explicit click, the same saved channel and a successful
   candidate check no older than 24 hours. A future clock or channel change
   invalidates that snapshot and requires a new check; an unsaved preview never
   chooses the request channel. There is no automatic download or cached/offline
   candidate restoration on restart.
3. Show Downloading, Verifying and Extracting progress. Protection, panic/PIN,
   settings editing and overlays remain active. No worker/journal preparation,
   process restart, executable replacement or settings backup occurs here.
4. **Cancel download** cancels the shared request and waits for stager cleanup.
   A late queued progress callback cannot overwrite the terminal status. If
   verification already completed when cancellation arrives, the result can be
   a verified ready download, never an installed update.
5. **Verified download ready** means only a separate verified stage. The UI
   explicitly says nothing was installed and installation is not connected in
   this build. **Discard download** removes only this process's returned owned
   stage under the same managed lease. It never accepts a UI/manifest path.
   While ready, further checks/downloads wait for discard, avoiding mismatched
   candidate/stage versions. Saving another channel does not reinterpret the
   retained stage or cancel an explicitly requested transfer.
6. Incomplete cleanup is disclosed. Further downloads in this process are
   disabled; unknown files and recovery evidence are preserved. A failed discard
   continues to report retained files, not successful removal. View releases and
   ordinary Exit remain available. Retrying discard after a temporary lock clears
   restores admission only when the same owned handle confirms complete removal;
   it never resets unknown stage or history evidence. Nothing claims installation
   success.

Closing settings to the tray does not cancel an explicit download. Reopening
settings binds to the same session/status. Application shutdown and update
suspension cancel and await requests plus complete UI event continuations before
disposing resources. Saved automatic-check opt-out does not cancel an explicitly
requested download. Suspension resume never automatically retries it.

## Shared admission, privacy and storage

Discovery, manual/tray/startup checks, downloads and owned-stage discard share
one dispatcher-owned single-flight session. A repeated/reentrant click joins
the admitted task; no second HTTP operation starts. Download uses the existing
30-second/24-hour startup scheduling policy only as shared admission metadata;
it never creates another timer or recurring worker.

Before download IO, preflight requires approved compiled trust and the exact
default private Windows x64 managed App/settings/launcher context. No-trust and
source/portable/custom installations stop before HTTP or staging. The private
history lease is held through admission and response bookkeeping. Pending intent
is written before request; a known global GitHub retry deadline blocks manual
downloads too. Download responses persist retry information with an independent
bounded finalization token even if cancellation races a response. Downloads do
not advance successful release-check timestamps or grant fresh-discovery apply
authority. Lost outcome writes retain pending evidence and fail closed as in
[phase 6b](update-check-scheduling.md).

The runtime takes `.update.lock`, validates the managed layout and archives only
an already terminal recovery journal before calling the existing managed stager.
An uncommitted operation blocks staging. This adapter performs disk/crypto/lease
work off the UI dispatcher and holds the lease across the complete stager call.
The stager reauthenticates exact signed metadata, hashes the streaming ZIP and
single payload, validates PE identity, checks disk capacity and uses private
fixed-name files; see [download boundaries](update-download.md).

Progress is throttled by the stager, marshalled to the UI, bounded to 0–100 and
ignored after task completion. A Ready progress event alone is never sufficient
to expose the verified-ready state. No package is buffered in RAM, and no raw
exception, URL, credential or user path is presented by the download status.

A completed stage can remain after exit/crash. No arbitrary startup scan restores
it as ready or grants apply authority. Existing lease-scoped retention may prune
verified stages older than 24 hours on subsequent managed maintenance; admission
stops at four stage entries rather than writing around unknown evidence. Discard
provides immediate cleanup for the owned handle. No extra scheduled cleanup or
unbounded recursive deletion is added.

## Evidence and next gates

New tests cover explicit-only admission, saved-channel/freshness checks,
single-flight/cancel/drain, response/cancel retry persistence, lost-history
outcomes, incomplete cleanup, truthful discard failure, opt-out vs manual work,
late progress and unconfigured runtime refusal. Compiled WPF tests cover connected
download/cancel/discard actions, shutdown handler drain, unchanged settings and
available/downloading/ready/failure layouts at multiple widths. Existing signed
real-EXE staging and isolated process-recovery suites remain separate evidence,
not live installed-App qualification.

Next: connect the returned owned stage to **Update now**, edit/Panic/restart
consent and the existing authenticated handoff; independently approve production
signing custody/compiled trust, then real signed App A-to-B and Windows 10/11
standard-user/fault qualification. No UI shortcut may bypass these gates.
