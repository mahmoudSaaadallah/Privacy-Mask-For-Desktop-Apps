# Controlled renderer packaging comparison

The [paired one-window results](renderer-packaging-results-beta18.md) and
[movement/reveal follow-up](renderer-motion-reveal-results-beta18.md) are recorded
separately. Transition-specific response and full-App qualification remain open.

The beta.18 full-App C/U/C experiment found a repeatable memory difference but
CPU increased in both U1 and the final compressed C2. Repeat short CPU/response
measurements with a fixed workload and alternating order before attributing that
drift to compression. This experiment uses the production capture/overlay code
with **owned synthetic windows**, not the installed App or personal profiles.

## Method

- Windows x64 and PowerShell 7.4+, a clean committed checkout, same SDK and device.
- Prepare two self-contained single-file fixtures, with no trimming or ReadyToRun.
  The five managed assemblies (including the fixture) must be byte-identical;
  runtime configurations must match. Hash every published file before running.
- Every pass starts a fresh process. Three pairs use C/U, U/C, C/U order per
  scenario. Warm up each process for 30 seconds, then measure 60 seconds at 500 ms.
  This reduces order bias; it does not eliminate unrelated desktop/GPU workloads.
- Reset timing histograms and the simulated movement/reveal phase after warmup.
  Track synchronous `UpdateOverlay` call duration, dispatcher tick intervals and
  intervals between new frame references. Fixed-size 1 ms histogram buckets give
  P95 upper bounds, with explicit overflow at the last bucket (2000 ms). Mean and
  maximum retain unbucketed values. Empty measurements are null, not zero latency.
- Require varied synthetic captures, fresh frames, measured frame progression,
  complete process-counter coverage and normal fixture exit. Closing a synthetic
  target aborts the run. A timer finishing alone is not evidence of live capture.
- Resource counters include the fixture targets and renderer in the same process.
  They exclude the full shell, updater, tray, discovery and real keyboard routing.
  Synchronous call duration is **not input-to-display latency**: WPF/GPU presentation
  is asynchronous. `RevealInputs` simulates reveal parameters, not Alt/hotkeys.
- Keep all ordinary workload/power/display settings stable. Do not run builds or
  other benchmarks during sampling. Keep Defender active. Do not force GC, trim
  working sets, change blur quality or relax budgets to obtain better numbers.

The runner refuses a running App/another renderer at each pass boundary and never
stops the installed App. Exit normally before the session; protection is off until
you choose to reopen it. It never installs a package or modifies settings, startup,
shortcuts, update history, trust pins or the production compression default.
It sends stop only to its owned fixture, with bounded cleanup on failure.

## Commands

Prepare first; this does not open windows:

```powershell
pwsh -NoProfile -File scripts/measure-renderer-packaging.ps1 `
  -OutputDirectory artifacts/renderer-pair-prepared -PrepareOnly
```

After normal App exit, in an agreed visible test session:

```powershell
pwsh -NoProfile -File scripts/measure-renderer-packaging.ps1 `
  -PreparedDirectory artifacts/renderer-pair-prepared `
  -OutputDirectory artifacts/renderer-pair-one-window `
  -Scenarios OneWindow -AllowVisibleFixtures
```

One scenario takes about nine minutes plus startup/cleanup. The default scenarios
are OneWindow, MoveResize and RevealInputs (about 27 minutes). IdleFixture,
TwoWindows and MoveOnly are available too. Use a new output directory every time.
Prepared packets must match the current commit and SDK. Separate native extraction
caches remain under the evidence directory; this is not a controlled cold-start test.

The result packet contains deployment identities, per-pass counter summaries,
admission/capture/response evidence, stderr and a final summary only after all
requested passes complete. Partial artifacts remain after failure but must not be
treated as a completed comparison. No screenshots, pixel buffers, personal profile
names or window titles are exported. Numeric instrumentation adds small, equal
overhead to both variants. Do not compare these counters directly to an older
uninstrumented or framework-dependent fixture.

## Acceptance and remaining gates

Keep individual pair/order results visible, including outliers and failed runs.
Treat CPU drift or opposing paired differences as inconclusive. Capture continuity
and similar timing alone do not prove equal visual quality or end-user response.
Unmeasured scenarios stay NotRun. The full-App installation/real shortcuts,
Panic/PIN, actual sign-in, sleep/resume, DPI/monitor changes, recovery and eight-hour
qualification remain separate gates. No automatic performance pass, packaging
change or Stable qualification follows from this experiment.

CI runs pure admission/response/packet rejection tests and histogram unit tests;
it does not open visible windows or assert performance budgets on shared runners.
