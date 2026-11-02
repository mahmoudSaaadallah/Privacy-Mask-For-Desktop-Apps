# beta.18 paired one-window renderer results — 2026-11-02

Later [movement/reveal measurements](renderer-motion-reveal-results-beta18.md)
are a separate session; the NotRun statements below describe this one-window run.

The uncompressed fixture used **74.523 MiB less private memory** and 126.137 MiB
less working set in this static one-window experiment. Mean CPU and observed
frame intervals were close between variants. These results support the memory
contrast under this workload; they do not establish faster rendering, general
CPU equivalence, real input/display latency or Stable qualification.

**Production compression and the installed App are unchanged.** The owner exited
the App normally before the test. Only owned synthetic targets and their overlays
ran; all fixtures exited normally at completion. The owner was told they could
reopen the installed App. No App was force-stopped, installed or automatically
restarted, and no release was published. Product version remains beta.18.

## Inputs and completeness

- Clean source `518e496124498728a82c0b294a17e56b08e6106e`, SDK 10.0.402,
  self-contained Core/Desktop runtime 10.0.13. Windows 11 build 26300, 20 logical
  processors, same physical owner device.
- Both single-file bundles contain byte-identical App/Core/Windows/Updates and
  renderer fixture assemblies, and matching runtime configurations. No trimming
  or ReadyToRun. The runner verified published payload hashes before each pass.
- One static synthetic target, **900 x 700**, Blur strength **0.7**, captured frame
  **36 x 28** in both variants. Requested renderer tick interval 100 ms.
  Capture sizing/quality and production refresh behavior were not modified.
- Six fresh-process passes: **C/U, U/C, C/U**. Each has 30 seconds warmup, then
  60 seconds process observation at 500 ms. Timing histograms and synthetic phase
  reset after warmup. A separate two-pass four-second smoke check succeeded first;
  smoke observations are excluded from the results below.
- All six counter passes completed, **695 samples** total, 115–116/pass;
  duration **60.030–60.503 s**, minimum coverage **0.9578**, maximum gap **535.9 ms**.
  Response intervals were 60.101–60.566 s, including the short command boundary
  around the counter observation. These are adjacent measurements, not precisely
  identical start/end timestamps.
- Every pass had varied capture pixels, **182–184 new frame references**, last
  observed frame age **0.06–0.29 s**, no response-histogram overflow and a normal
  fixture exit. No frames/screenshots were exported.
- No builds/tests ran during sampling; no forced collection, working-set trim,
  protection changes, antivirus exclusions or quarantine restoration were used.
  Ordinary desktop/GPU workload was not independently controlled. This was not a
  visual quality review, real keyboard test or cold-start experiment.

## Individual passes

CPU is percentage of total logical-processor capacity. Memory is MiB. Timing
columns are means in milliseconds; the frame metric observes new frame references
in the overlay, not physical display presentation.

| Order | Variant | Working set | Private | CPU % | Handles | Update call ms | Tick interval ms | Frame interval ms | Frames |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 / pair 1 | Compressed | 281.59 | 167.68 | 0.544 | 974 | 0.01567 | 109.715 | 329.197 | 184 |
| 2 / pair 1 | Uncompressed | 155.82 | 93.05 | 0.539 | 973 | 0.01453 | 109.773 | 329.351 | 183 |
| 3 / pair 2 | Uncompressed | 155.84 | 93.68 | 0.556 | 974 | 0.01551 | 109.889 | 329.670 | 183 |
| 4 / pair 2 | Compressed | 281.99 | 167.85 | 0.581 | 975 | 0.01589 | 110.058 | 328.982 | 184 |
| 5 / pair 3 | Compressed | 282.48 | 168.39 | 0.630 | 974 | 0.01764 | 109.933 | 329.816 | 182 |
| 6 / pair 3 | Uncompressed | 155.99 | 93.62 | 0.594 | 973 | 0.01684 | 110.253 | 330.725 | 183 |

Means of the three pass means:

| Metric | Compressed | Uncompressed |
| --- | ---: | ---: |
| Working set MiB | 282.020 | 155.883 |
| Private memory MiB | 167.973 | 93.450 |
| CPU % | 0.585 | 0.563 |
| Synchronous update call ms | 0.01640 | 0.01563 |
| Dispatcher tick interval ms | 109.902 | 109.972 |
| Observed frame interval ms | 329.332 | 329.915 |

Uncompressed private-memory savings by pair were 74.63, 74.17 and 74.77 MiB.
CPU differences (U minus C) were -0.005, -0.025 and -0.036 percentage points.
The small CPU differences, slight upward drift and three pairs do not establish
a speed improvement or statistical equivalence. The earlier full-App idle CPU
drift remains unexplained: this fixture does not run discovery, tray or updater.

Update-call P95 upper bounds were 1 ms for every pass; the histogram cannot resolve
sub-millisecond percentile differences. Tick P95 bounds were 119–124 ms and frame
P95 bounds 342–347 ms. Call duration is **not the total capture/GPU/presentation
cost**: work can run asynchronously. Observed frame intervals are not keystroke,
Hover, reveal or input-to-display latency. Similar values do not prove equal visual
quality, leak freedom or absence of regressions in other scenarios.

Do not combine these private-memory values with the earlier full-App tray means.
The workloads and instrumentation differ. The previous full-App experiment's
measured EXE/ZIP trade-off still applies to that packet; this session did not
remeasure installed allocation, caches, staging or rollback space.

## Security observations and remaining gates

Defender reported service, antivirus, real-time and behavior monitoring enabled,
mode Normal, definitions 1.463.84.0 before smoke, after smoke and after measurement.
The bounded 1116/1117 event query found **no matching PrivacyMask detection events**
in this session. These are endpoint observations, not proof of continuous
protection, universal antivirus clearance or resolution of the beta.17 detection.

Qualification remains **NotEvaluated**. Matched MoveResize, RevealInputs, IdleFixture,
TwoWindows and MoveOnly comparisons are **NotRun in this session**. Full-App CPU
attribution, real shortcuts/Alt/Hover/PIN, visual checks, startup latency, sleep,
monitor/DPI changes, update/recovery and eight-hour gates remain separate work.
Next: repeat paired movement/resize and simulated reveal workloads before deciding
whether to propose changing production packaging. No budget was relaxed.

## Evidence and validation

Ignored local evidence, not uploaded with this document:

- `artifacts/renderer-pair-beta18-20261102/prepared.json`
- `artifacts/renderer-owner-session-20261102-122831-377f09af/session.json`
- The same session's `smoke/`, `one-window/` and `analysis.json`.

Fixture EXE SHA-256:

- Compressed: `e5b4c2a5e66548e1af2b3f7c9a6746d3c1c4872464aa48a890df62a8698a48ed`
- Uncompressed: `6155f8640f36242794e801af4a149cbf17a20118e260c3d02748140c3a4be11c`

Tool validation before the session: 942 unit tests (including five histogram
cases), 21 offline paired-runner checks, renderer opt-in/argument regressions,
57 PowerShell syntax checks and all four CI jobs on the exact measurement source
(run 41443456693). The [runbook](renderer-packaging-comparison.md) describes scope,
commands, rejection checks and interpretation limits.
