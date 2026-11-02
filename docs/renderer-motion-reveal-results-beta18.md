# beta.18 paired movement/reveal results — 2026-11-02

The uncompressed renderer fixture used **75.723 MiB less private memory during
MoveResize** and **74.853 MiB less during RevealInputs**. CPU differences changed
sign between pairs; this session does not establish a speed improvement or CPU
equivalence. Frame intervals include intentional capture suppression/invalidation,
so they cannot qualify reveal/recovery latency.

All 12 measured passes completed and their owned windows exited normally. The
installed App, production compression, updater and settings were unchanged. No
release, version bump or eight-hour run occurred. The owner was told they could
reopen the App after the session. Qualification remains **NotEvaluated**.

## Matched experiment

- Source `c63d276071c89421412d49b6888c90556d7377fe`, clean checkout; SDK 10.0.402,
  bundled Core/Desktop runtime 10.0.13; Windows 11 build 26300, 20 logical CPUs.
  This source adds documentation to the earlier one-window measurement source;
  new bundles were prepared to retain exact source/version admission.
- Byte-identical five managed assemblies and matching runtime configuration
  between compressed/uncompressed variants; single-file, self-contained, no
  trimming or ReadyToRun. Published payload hashes checked before each pass.
- One synthetic target and real production capture/overlays, Blur strength 0.7,
  nominal renderer tick 100 ms. No real App shell, settings, tray, discovery,
  updater or keyboard routing ran. No personal content was captured/exported.
- MoveResize alternates two seconds of resizing/movement with two seconds of
  settling. Resize invalidates capture; safe fallback can remain until settling.
  RevealInputs simulates cursor movement and alternates held reveal every two
  seconds; capture is intentionally suppressed during held reveal.
- Each scenario used fresh-process C/U, U/C, C/U pairs, 30 seconds warmup then
  60 seconds sampled at 500 ms. Timing/phase reset after warmup. Four separate
  four-second smoke passes succeeded first and are excluded from results.
- **1,386 counter samples** across 12 passes; 115–116/pass, durations
  **60.007–60.440 s**, minimum coverage **0.9564**, maximum sampling gap **554.8 ms**.
  Response intervals **60.060–60.469 s** include command-boundary overhead and are
  not exactly the same interval as process counters.
- MoveResize observed 96–100 new frame references/pass; RevealInputs 90–91.
  All passed varied-frame/progression/final-age checks and normal-exit checks.
  This is evidence of capture activity, not a visual fidelity or input-latency pass.
- No builds/tests, forced GC, working-set trim, installation or antivirus changes
  ran during measurement. Ordinary desktop/GPU conditions were not independently
  controlled. Counters include both renderer and synthetic target painting.

## Individual passes

Memory is MiB; CPU is percent of total logical-processor capacity. Update and
frame columns are mean milliseconds. Frame timing observes references in the
overlay, not physical display presentation.

| Scenario | Pair / order | Variant | Working set | Private | CPU % | Handles | Update call ms | Frame interval ms | Frames |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| MoveResize | 1 / 1 | Compressed | 289.77 | 182.59 | 1.388 | 985 | 14.410 | 590.158 | 99 |
| MoveResize | 1 / 2 | Uncompressed | 163.97 | 105.72 | 1.395 | 984 | 14.821 | 582.659 | 100 |
| MoveResize | 2 / 3 | Uncompressed | 165.71 | 106.64 | 1.176 | 1010 | 12.846 | 596.141 | 98 |
| MoveResize | 2 / 4 | Compressed | 289.37 | 182.89 | 1.234 | 984 | 13.901 | 609.858 | 96 |
| MoveResize | 3 / 5 | Compressed | 289.65 | 180.23 | 1.230 | 987 | 13.743 | 585.308 | 100 |
| MoveResize | 3 / 6 | Uncompressed | 165.65 | 106.18 | 1.163 | 1010 | 13.096 | 595.996 | 98 |
| RevealInputs | 1 / 1 | Compressed | 289.64 | 172.67 | 0.438 | 977 | 0.378 | 648.010 | 90 |
| RevealInputs | 1 / 2 | Uncompressed | 164.36 | 98.22 | 0.418 | 976 | 0.367 | 648.812 | 90 |
| RevealInputs | 2 / 3 | Uncompressed | 164.33 | 98.44 | 0.499 | 977 | 0.375 | 647.228 | 90 |
| RevealInputs | 2 / 4 | Compressed | 290.20 | 173.41 | 0.392 | 977 | 0.345 | 665.506 | 91 |
| RevealInputs | 3 / 5 | Compressed | 59.02 | 172.83 | 0.465 | 1002 | 0.375 | 666.469 | 91 |
| RevealInputs | 3 / 6 | Uncompressed | 163.88 | 97.69 | 0.445 | 981 | 0.430 | 665.840 | 91 |

Means of three pass means, without excluding outliers:

| Scenario / variant | Private MiB | CPU % | Update call ms | Tick interval ms | Frame interval ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| MoveResize / compressed | 181.903 | 1.284 | 14.018 | 125.400 | 595.108 |
| MoveResize / uncompressed | 106.180 | 1.245 | 13.588 | 125.181 | 591.599 |
| RevealInputs / compressed | 172.970 | 0.432 | 0.366 | 110.008 | 659.995 |
| RevealInputs / uncompressed | 98.117 | 0.454 | 0.390 | 110.150 | 653.960 |

Private-memory savings by pair: MoveResize 76.87, 76.25, 74.05 MiB; RevealInputs
74.45, 74.97, 75.14 MiB. CPU differences (U minus C): movement +0.007, -0.058,
-0.067 percentage points; reveal -0.020, +0.107, -0.020. Opposing differences and
temporal drift prevent a general speed/equivalence claim. Short handle counts
also differ and do not establish leak freedom.

**Working-set anomaly retained:** compressed RevealInputs pair 3 averaged only
59.02 MiB resident, versus 289.64/290.20 in the preceding compressed passes, while
private commit stayed 172.83 MiB. The evidence does not identify the cause. No
working-set trimming was requested by the harness. Do not treat the resident
drop as a packaging win or silently discard that pass; private commit and resident
RAM are different quantities. Do not pool these workloads with full-App tray data.

## Timing limits that remain open

All 12 frame-interval P95 values reached the histogram's final 2000 ms bucket;
the numeric P95 upper bound is **null with overflow=true**, not zero or a capped
2000 ms latency. Exact recorded maximum gaps were 2428.102–2463.199 ms during
movement and 2404.548–2463.418 ms during reveal. The scenarios deliberately contain
two-second resize/reveal phases. Existing renderer code invalidates resized
requests and suppresses capture during held reveal; these aggregates mix those
phases with active capture. They cannot isolate time from settling/re-hide to
the first usable frame or prove uninterrupted blur while resizing.

Update-call P95 upper bounds were 42–46 ms for movement and 1–2 ms for reveal.
Tick P95 bounds were 157–161 ms and 118–125 ms respectively. Synchronous call
duration excludes asynchronous capture/GPU/presentation work. Final frame ages
were 0.07–0.59 s (movement) and 0.19–2.28 s (reveal), so a final-age gate passing
does not imply low transition latency.

Next response validation must distinguish the resize/reveal phases and check
safe fallback plus first-frame recovery; repeating these aggregate timing numbers
alone cannot close that gate. Actual Alt/hover/hotkey input, visual checks,
multiple windows, idle/move-only comparisons, full-App CPU attribution, startup,
sleep/DPI/monitor behavior, update/recovery and eight-hour qualification remain
separate work. No production packaging change or budget relaxation is justified
solely by these passes.

## Security and evidence

Defender service, antivirus, real-time and behavior monitoring were enabled,
mode Normal, definitions 1.463.84.0 before smoke, after smoke and at completion.
The bounded 1116/1117 query found no matching PrivacyMask detections in the
session. Endpoint evidence does not establish continuous protection or resolve
the historical beta.17 detection. No exclusions, restores or security changes.

Ignored local artifacts, not uploaded:

- `artifacts/renderer-motion-pair-beta18-20261102/prepared.json`
- `artifacts/renderer-motion-owner-session-20261102-125436-b2dadb12/` containing
  `session.json`, `analysis.json`, `smoke/` and `motion-reveal/` pass evidence.

Fixture EXE SHA-256:

- Compressed: `ba2235eacf682ba9cf95ae1c71db8dfd75ba654a384d96a0df60248ec3526185`
- Uncompressed: `1e687d0e2ea3bb6234b462031c424ef123ced89282c1a888e498917e721ac274`

All four CI jobs passed on exact measurement source c63d276 (run 41446760138).
The [runbook](renderer-packaging-comparison.md) and earlier
[one-window results](renderer-packaging-results-beta18.md) retain their distinct
scope; this follow-up adds observations without changing measurement code.
