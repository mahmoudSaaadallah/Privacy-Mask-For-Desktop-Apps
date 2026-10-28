# Live renderer experiment and beta.17 movement fix — 2026-10-28

## Authority and scope

After merged PR #65 (9286046f8a51b013eb40024543a6db17f8b4f5d2), the owner
explicitly started the agreed synthetic-content session on their device. No
PrivacyMask.App process was running at admission; none was terminated, installed
or reconfigured. The fixture does not start production App/Shell, discovery,
global hotkeys, startup registration, settings IO or HTTP.

The benchmark opens only its own native WinForms test windows and the production
PrivacyOverlayWindow on top. HWNDs are verified as belonging to that process.
PrintWindow captures only these opaque synthetic surfaces. No desktop image,
personal window/title, PIN or settings is captured/exported. Counter reports are
local ignored artifacts; no image is written. Closing a fixture aborts the run.

Windows Computer Use initialization failed repeatedly, including after reset:
`failed to write kernel assets: The system cannot find the path specified (os error 3)`.
No alternative desktop-input/screenshot automation was used. The owner confirmed
seeing the original synthetic window and clear blur. This is user-reported visual
confirmation, not an automated screenshot/keyboard/monitor qualification pass.

## Finding and bounded fix

The original capture request keyed its identity and frame reuse on full screen
bounds. PrintWindow already reads only window-local width/height; it never uses
those Left/Top coordinates. Continuous translation invalidated the frame every
tick and cleared it before the next render could show the asynchronously completed
capture. Safe fallback remained, but real blur could not settle while dragging.

An isolated **MoveOnly** reproduction on beta.16 produced zero displayed capture
frames, no pixel-variation proof and a safe rejected admission after **20.24 s**,
with the window fixed at 900x700. It was not counted as a performance pass.
Evidence: `artifacts/live-move-before-20261028/MoveOnly-admission.json`.

The production change normalizes capture-request bounds to `(0,0,width,height)`.
Actual on-screen overlay placement/geometry continues using the original screen
bounds. Window identity, dimensions, style and downsample-factor comparisons
remain; resizing still invalidates capture. Minimization/full occlusion/Panic
and capture timeouts/limits still take their existing paths. No interval, blur
strength, quality, alpha conversion, key trust or settings schema changes.

Focused beta.17 reproduction became capture-ready at **0.87 s**, then observed
**32** distinct displayed frame objects by **11.59 s**, with RGB variation,
36x28 output and a fresh last frame. This ten-second run is functional evidence,
not a long-duration resource comparison.
Evidence: `artifacts/live-move-after-20261028/MoveOnly-summary.json`.

Four regression cases cover translated origins (including negative screen
coordinates) preserving local identity and width/height changes still changing
identity. Existing lifecycle, health, appearance and restart tests remain required.
Source version is 2.0.0-beta.17 / FileVersion 2.0.0.17; no publication is implied.

## Protocol and evidence limits

Developer PowerShell 7, unlocked Windows desktop, and explicit permission for
temporary visible synthetic windows are required:

```powershell
pwsh -NoProfile -File scripts/measure-renderer-baseline.ps1 `
  -AllowVisibleFixtures -OutputDirectory artifacts/live-renderer-new-session
```

The output directory must be new. Each scenario gets a fresh process, 30 seconds
warm-up, three consecutive 60-second passes at 500 ms. Tick interval is the
production 100 ms; fixture blur strength is 70% and never persisted. Per-pass
checks require varied non-stale frame objects and reject fallback-only output.
Admission and completed-scenario JSON are retained even if a later case fails.

- IdleFixture is one synthetic native window without a mask, **not tray idle**.
- OneWindow/TwoWindows exercise static real captures and overlays.
- MoveOnly continuously changes position at fixed physical size.
- MoveResize alternates two seconds of resizing with two seconds settling.
  Continuous resizing without settle can correctly remain on safe fallback;
  the original nonstop-resize attempt was rejected and is not marked Pass.
- RevealInputs varies the renderer's cursor/reveal parameters, **not actual
  mouse/Alt/hotkey input or the exclusive 500 ms gesture detector**.

Resource counters include both renderer and synthetic WinForms targets in the
same process. In particular target OnPaint/PrintWindow work contributes CPU;
do not label these numbers full-App CPU, apply tray/two-external-app budgets,
compare with the hidden-GUI harness, or claim generic RAM reduction. Process
identity/version, sample coverage and live frame counts are retained. Actual
third-party matching/window inspection, physical keyboard, real per-monitor DPI,
sleep/resume and eight-hour stability remain NotRun.

The original beta.16 baseline collected nine complete resource passes for idle,
one and two windows, then correctly aborted the movement admission. It has no
successful aggregate summary and the early tool did not archive its status
snapshots; those raw counters are directional reference only, not a completed
qualification packet. The updated tool retains partial/admission evidence.

The production fix addresses redundant invalidation/continuity, **not a proven
general RAM reduction**. Resource optimization beyond this finding needs its
own attribution and matched comparison; no old budget is silently relaxed.

## Results and remaining gates

The five active-renderer scenarios completed **15 full passes** on Windows 11
10.0.26300, i9-7900X / 20 logical processors, SDK 10.0.402. Source was
`d3f8aa9986656888b976bdcd11621837d98e4518`, clean at admission. Product was
`2.0.0-beta.17+d3f8aa9986656888b976bdcd11621837d98e4518`; App DLL SHA-256:
`f0e549583596ab1fa0b72eb6c6335f1d6a8d0ccae88374ea313d0db35f5ba005`.
Evidence: `artifacts/live-renderer-beta17-20261028/summary.json`.

Means of three per-pass averages, including the synthetic targets:

| Scenario | Working set MiB | Private MiB | Whole-machine CPU % | Handles | Final distinct frames per window |
| --- | ---: | ---: | ---: | ---: | --- |
| OneWindow | 151.64 | 96.37 | 0.548 | 910.0 | 643 |
| TwoWindows | 159.77 | 108.66 | 1.130 | 914.7 | 640 / 640 |
| MoveOnly | 156.22 | 96.50 | 0.867 | 906.7 | 623 |
| MoveResize | 156.71 | 104.90 | 1.246 | 912.3 | 341 |
| RevealInputs | 155.90 | 95.78 | 0.400 | 905.0 | 316 |

Each active pass lasted 60.043–60.525 seconds, with 116–117 samples, minimum
coverage 0.9648 and maximum sample gap 564.4 ms. All retained capture checks
passed; translation preserved fresh frames, resizing invalidated captures and
regained real blur during settle. Final frame ages were 0–2.39 seconds across
cases; RevealInputs intentionally suppresses capture during held reveal.
No heavy build/test workload ran concurrently with measurement. Other normal
desktop workloads were not fully controlled. These numbers establish a local
reference, not a paired resource saving or eight-hour leak/budget qualification.

The separate IdleFixture reference completed three further passes, with no
captured frames as expected: working set **74.09 MiB**, private **21.69 MiB**,
whole-machine CPU **0.019%**, handles **355.3**. Evidence:
`artifacts/live-renderer-beta17-idle-20261028/summary.json`. It used the same
source commit/product/SDK, with documentation-only working-tree edits at
admission (the report correctly says dirty). Its separately built App DLL hash
is `aab27a8febc0edf518e2b194abf91099154db05a5a43f7a7a6b70c8933c12f46`;
it is not presented as the exact same compiled binary as the active run.

Local delivery validation passed:

- `scripts/verify.ps1`: **925** unit tests, zero-warning/error solution build,
  **40** PowerShell syntax checks, existing policy/installer/rollback and compiled
  GUI fixtures. Simulated layout scales are not real per-monitor DPI testing.
- Renderer argument admission rejects missing opt-in/invalid duration/scenario
  before creating windows; CI runs these gates without visible fixtures.
- **12** process-bound performance-probe regressions, all three short isolated
  GUI-runner cases and argument/timer/overwrite regressions.
- Self-contained App plus Native AOT launcher package; isolated temporary fresh
  install/shortcut/uninstall smoke, with no production App execution.
- **25** real-EXE signed-package checks, using only an ephemeral in-memory test
  key; offline preparation, staging and retention checks do not install updates.

Package footprint: ZIP **74,304,882 bytes** (70.863 MiB), App EXE
**75,802,937 bytes** (72.291 MiB), launcher **8,986,624 bytes** (8.570 MiB),
extracted package **84,821,260 bytes** (80.892 MiB), fresh installed files
**84,789,677 bytes** (80.862 MiB), shortcuts separately **6,506 bytes**.
These are logical bytes before first App launch, excluding runtime caches,
user state, staging/rollback and filesystem allocation. The historical 65 MiB
single-App budget remains unmet; it was not relaxed. No package-size reduction
is claimed. Evidence: `artifacts/beta17-live-renderer-package-20261028/footprint.json`.
App SHA-256: `305dae91c7fe7c3dafab47bdd10be4ecdd8338d3f87aa1f6c39da87b8833fb28`;
ZIP SHA-256: `8074de763d89ba1832d2778b86bc303651217f54d7778d30d685a4812aa6ca5e`.

Stable qualification stays blocked on full-App resource budgets and real-device
keyboard/Hover/Alt, PIN, third-party windows, 25 settings/pause cycles,
sleep/resume, multiple monitors/DPI, live recovery and eight-hour stability.
The owner's previously reported beta.14-to-beta.15 update remains a successful
user-reported trial, not a substitute for those recovery/compatibility gates.

No real PINs/profiles were touched, no production private key used, and no
release/GitHub settings or published history changed. Merge and release approval
remain with the owner.
