# beta.18 full-App packaging comparison — 2026-11-02

The installed full App used **81.155 MiB less private memory (43.26%)** without
single-file compression in this short fresh-tray C/U/C session. Working set was
133.801 MiB lower. Compressed memory returned close to the first baseline after
restoration. This supports a packaging-related memory difference in this
scenario; it does not establish savings for all workloads or a leak fix.

**Production compression remains enabled.** The owner's final installation was
restored to the matched compressed diagnostic and left running. No release was
published, no resource budget was relaxed, and `2.0.0-beta.18` / FileVersion
`2.0.0.18` remain unchanged by this documentation delivery.

## Matched inputs and method

- Reviewed clean source: `d42ad03ef1ab83b63410a206ee4ff8540b31f1a6`.
- Trial ID: `35e44c7ef09844d5a1991c68eb47d641`.
- SDK 10.0.402; both bundles contain Core/Desktop runtime 10.0.13.
- Windows 11 build 26300, 20 logical processors, same physical owner device.
  Observer: PowerShell 7.6.5 / runtime 10.0.11.
- Self-contained single-file App, no trimming or ReadyToRun. Compression is the
  only requested packaging difference. Preparation required byte-identical
  App/Core/Windows/Updates managed assemblies and matching runtime configuration.
- The Native AOT launcher was built once and shared by both variants; SHA-256
  `6923e134becc43c7eacc5e6ba380c056fcdc7d5eedac3612d5a8fa854b7d1a56`.
- C1 compressed, U1 uncompressed, C2 compressed again. The owner exited normally
  through Tray > Exit between stages; the helper confirmed no running App and
  verified same-product/no-downgrade admission before installation.
- Each stage starts a fresh minimized App, warms up 30 seconds, then observes
  three consecutive 60-second passes at 500 ms. No additional warmup between
  passes in one process. Counters cover the App process, not other applications.
- State was **owner-declared**: settings closed, protected windows closed or
  minimized, same settings and foreground workload. This was not independently
  verified by native UI automation. No settings/PIN/window contents were read.
- No build/test workload ran during sampling; no forced GC, working-set trim,
  renderer change, antivirus exclusion or quarantine restoration was used.

All nine passes completed: **1,080 samples**, 120/pass; durations
**60.019–60.040 s**, minimum coverage **0.9993**, maximum gap **526.612 ms**.
The offline comparator rechecked the numeric CSV, recomputed means, checked
process/start/version/hash identity, stage order, timing and completeness.

## Results

Means below average the three per-pass means in each stage. CPU is percent of
the total logical-processor capacity, not percent of one core.

| Stage | Working set MiB | Private MiB | CPU % | Handles |
| --- | ---: | ---: | ---: | ---: |
| C1 compressed | 313.527 | 187.730 | 0.2655 | 1061.17 |
| U1 uncompressed | 180.252 | 106.424 | 0.7977 | 1057.77 |
| C2 compressed restored | 314.578 | 187.428 | 0.7700 | 1057.72 |

Across the six compressed passes versus the three uncompressed passes:

| Metric | Compressed C1+C2 | Uncompressed U1 |
| --- | ---: | ---: |
| Working set MiB | 314.053 | 180.252 |
| Private memory MiB | 187.579 | 106.424 |
| CPU % | 0.5177 | 0.7977 |
| Handles | 1059.44 | 1057.77 |

Compressed C1/C2 private means differ by only about 0.302 MiB, while U1 is about
81.155 MiB lower. Working set also returns near the starting baseline, but
residency can vary with system memory pressure and is not a guaranteed saving.

**CPU attribution is inconclusive.** U1's CPU mean is higher than the pooled
compressed mean, but compressed C2 also rises from 0.2655% in C1 to 0.7700%, close
to U1's 0.7977%. This session cannot separate packaging effects from temporal or
workload drift. Do not call the candidate faster, CPU-neutral, or a confirmed CPU
regression. Controlled repeat CPU/response/startup measurements remain necessary.
Similar handle means over this interval do not establish absence of leaks.

Query-only region snapshots averaged private executable **committed virtual**
mappings of 78.098 MiB in C1, 0.008 MiB in U1 and 78.085 MiB in C2. This accompanies
the packaging contrast; mapping categories are not physical RAM or an allocator
identity. Do not sum them into the working set or infer exact native/managed heap
sizes. No memory contents or addresses were collected.

## Disk/download trade-off

| Logical bytes | Compressed | Uncompressed |
| --- | ---: | ---: |
| App EXE | 75,802,772 | 173,900,471 |
| Complete extracted diagnostic package | 84,816,734 | 182,914,435 |
| ZIP | 74,303,001 | 75,932,289 |

The uncompressed EXE grows **93.553 MiB**; the ZIP grows **1.554 MiB**. These are
measured payload/archive bytes, not total installed allocation. Settings, native
extraction caches, staging, rollback copies, filesystem overhead and snapshots
are additional. Fresh installer footprint and protected-window/GUI workloads
were not requalified in this session.

App SHA-256:

- Compressed C1/C2: `9a00270c75c2ad002ca26a2d611037fb3465a8dc65778311117a76ed8a5fa05d`.
- Uncompressed U1: `9c81a5be21c111cadef63de286a55f7ab952c4047c0646ec5ffc2ec91469564d`.

## Security observations and limits

Before/after each stage, Defender reported antivirus, real-time protection,
behavior monitoring and service enabled, mode Normal, definitions 1.463.84.0.
The event query covering the whole session found **no matching PrivacyMask
detection/quarantine events** (1116/1117). No background security polling timer
ran during counter sampling. Endpoint checks are not proof of uninterrupted
protection or universal antivirus clearance.

The original beta.17 packet was not restored or reused. Its detection remains
unclassified. This new beta.18 trial and the prior two short startup observations
do not prove that the original report was a false positive or permanently fixed.

Qualification stays **NotEvaluated**. Actual sign-in, blur/hover/Alt/PIN response,
visible protected windows, movement/resize, multiple monitors/DPI, controlled CPU
and startup latency, sleep/resume, recovery and the eight-hour run remain outside
this session. The memory saving is promising enough to justify those next tests;
it is not, on its own, approval to change production packaging or publish Stable.

## Evidence

Ignored local artifacts, not uploaded with the report:

- Prepared pair: `artifacts/full-app-packaging-beta18-20261102/trial.json`.
- Session: `artifacts/beta18-packaging-session-20261102-090610-8b5df81f/` with
  `comparison.json`, `analysis.json`, stage paths and per-stage security records.
- C1: `artifacts/full-app-trial-C1-20261102-090615-8bf2021c/`.
- U1: `artifacts/full-app-trial-U1-20261102-091216-7581307a/`.
- C2: `artifacts/full-app-trial-C2-20261102-091819-52464255/`.

Every stage includes admission metadata, numeric CSV, summary, offline HTML and
bounded region aggregates. C2's installed App and common launcher hashes were
verified at the end; the compressed App remained running. Use the
[trial runbook](full-app-packaging-trial.md) for methodology and the
[startup review](startup-launcher-review-beta18.md) for the beta.18 source changes.
