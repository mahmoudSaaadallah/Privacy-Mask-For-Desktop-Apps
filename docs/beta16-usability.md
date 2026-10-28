# Beta.16 usability delivery and next gates

## Delivery boundary

Current source is 2.0.0-beta.16 / FileVersion 2.0.0.16. This PR implements the
first, usability-focused stage of the owner-approved development plan. Repository
separation/private visibility remains deferred. Publishing, merging, production
signing and GitHub configuration changes are not part of this source delivery.

Settings/history/updater schemas remain 13/1/1. Trust, release repository,
mandatory daily cadence, server cooldowns, hotkeys, Alt's 500 ms exclusive hold,
600x70 hover defaults and startup preferences are unchanged. No dependency added.

## Behavior and acceptance

- One primary stage explains checking, transfer, package/application verification,
  readiness, safe deferral, preparation, restarting, failure and recovery.
  Download/progress/restart cannot claim that an installation committed.
- Restart blockers retain priority: Panic, edits, then window inspection. Names
  deduplicate by case-insensitive profile identity; equal display names may belong
  to different profiles. Three bounded plain-text labels plus remaining count
  avoid unbounded display. Names/titles/paths are not persisted or transmitted.
- Paused/focus-aware matching windows still block while visible and not minimized.
  Every window must be closed/minimized. Partial/failed enumeration fails closed.
  Existing stage pulses refresh the message; safety is rechecked independently
  during restart draining, authenticated handoff and final shutdown admission.
- Review applications only navigates to the normal list with its existing editor
  validation/focus behavior; it never minimizes, disables or saves anything.
- Unsupported build/system/architecture, outside managed path and failed managed
  verification get distinct guidance without HTTP or ownership bypasses.
  Outside-path classification does not assert a source-vs-portable distinction.
- Protection paused is distinct from stopped/disabled/focus-waiting/Panic.
  Information icons retain native hover and accessible help text; explicit F1
  opens help on all sidebar pages, while page focus alone never opens a popup.
  Release notes remain read-only, bounded plain text with no active markup.

## Verification record

The 921-test Release suite passed before the version-only bump. Compiled WPF
checks passed all six pages at simulated 100/125/150/200% DIP work areas and
12 update presentation states at 860/1000/1400 DIP widths, including long/repeated
names, review navigation, plain-text notes, explicit F1/focus-loss behavior and
unchanged settings. Rasterized synthetic screenshots were inspected locally;
they are not screenshots of a live protection session or actual monitor changes.
Final versioned verification additionally passed:

- scripts/verify.ps1: 921 Debug unit tests, solution build with zero warnings/errors,
  35-script PowerShell syntax validation, 55 version-policy, 82 mocked publisher,
  8 mocked source-gate, 95 mocked environment, 62 secret-guide and 82 isolated
  qualification-packet checks; installer transaction/entrypoint and compiled GUI
  checks passed. These are fixtures, not external release operations.
- 76 manifest-schema checks; settings/history/updater contracts remain unchanged.
- Local self-contained App + Native AOT launcher package built successfully and
  passed isolated install/uninstall, shortcut and package-identity smoke checks.
- 25 real-EXE signed package checks passed with an ephemeral memory-only test key,
  including authenticated preparation/staging; no production key or App execution.
- 38 signed real-process transaction cases passed with synthetic test peers,
  including rollback/recovery faults; no user installation was modified.

The local package's App was built from code commit 163d5f6; subsequent documentation
does not change product code. It is a test artifact, not a published release or
an instruction to replace the friend's installed beta.15 manually. CI rebuilds
the exact PR revision. Runtime/performance measurements are not claimed here.

## Stage 2: fresh performance baseline (not run by this delivery)

Use the existing runtime/size probes on an agreed synthetic Windows test target.
Compare exact source identities with the same SDK, machine, package model and
settings. Per short scenario: warm up 30 seconds, then three 60-second passes at
500 ms intervals. Record working/private memory, CPU, handles, EXE/ZIP sizes and
installed footprint separately. Exercise idle, Settings, one/two protected windows,
movement/resize, hover/Alt and 25 Settings/25 pause-resume cycles.

Inspect capture/overlay lifetime, subscriptions and existing bounded caches;
only repeatable measured findings justify optimization. Do not force GC, reduce
blur quality/responsiveness or silently relax existing budgets. Later performance
changes get a new Preview version, never replacement assets under an old tag.

## Stage 3: stability and stable-release gate (NotRun / pending)

Live keyboard/screen-reader, actual mixed-monitor DPI, Sleep/Resume, protected-app
restart, real PIN paths and the eight-hour resource run remain pending. Fault
injection belongs to an approved disposable target, not the friend's daily setup.
Existing synthetic transport/signature/transaction tests are not real-device
recovery qualification. The owner's beta.14-to-beta.15 confirmation is reported
single-device evidence only; see update-preview-trial.md.

Stable activation/publication remains blocked pending these gates and separate
owner approval. No missing test is recorded as Pass.

## Git delivery

One coherent change per commit on codex/beta16-update-usability; the owner merges.
The owner's latest instruction replaces the earlier Claude co-author footer on
all unpublished task commits with exactly:

```text
Committed-By-Agent: codex
Co-authored-by: codex <noreply@openai.com>
Co-authored-by: Codex <codex@openai.com>
```
