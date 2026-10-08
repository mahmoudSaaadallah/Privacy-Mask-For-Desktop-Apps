# Performance Baseline

This document records the repeatable performance measurements used to evaluate
PrivacyMask changes. Measurements must be compared on the same machine and with
the same scenario; a single sample is not a release guarantee.

## Reference environment

- Captured: 2026-10-07
- Operating system: Windows 11 Pro, build 26300
- Processor: Intel Core i9-7900X, 20 logical processors
- Memory: 31.7 GB
- SDK: .NET SDK 10.0.104
- Measured app: PrivacyMask 1.0.0 single-file build
- Measured product commit: `9a6c1155063f877448bee61b5deef548c1023bb9`

The measured product commit predates the About dialog and is suitable as an
initial runtime reference. Future pull requests must capture a fresh baseline
from their own commit before claiming an improvement.

## Initial runtime sample

The existing PrivacyMask process was sampled for 30 seconds at one-second
intervals. The settings window was not opened by the measurement script. The
number and activity of supported third-party app windows were not controlled,
so this is an observational baseline rather than a laboratory benchmark.

| Metric | Average | P95 | Maximum |
| --- | ---: | ---: | ---: |
| Working set | 87.90 MB | 88.62 MB | 88.62 MB |
| Private memory | 172.16 MB | 172.16 MB | 172.16 MB |
| Whole-machine CPU | 0.023% | 0.152% | 0.154% |
| Handles | 917 | 917 | 917 |
| Threads | 29 | 29 | 29 |

Run the same probe against the only running PrivacyMask process:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\measure-runtime.ps1 `
  -DurationSeconds 30 `
  -SampleIntervalMilliseconds 1000
```

When more than one instance or build is running, target a process explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\measure-runtime.ps1 `
  -TargetProcessId 1234 `
  -DurationSeconds 30
```

Use `-OutputPath` to save a machine-readable JSON summary. The runtime probe
records process resource counters and binary version information. It does not
read window titles, message content, or application settings.

## Initial distribution sample

| Distribution | Files | Size |
| --- | ---: | ---: |
| Current self-contained portable folder | 474 | 171.271 MB |
| Current self-contained single file | 1 | 72.014 MB |
| Framework-dependent publish, no symbols | 6 | 0.283 MB |

The framework-dependent number is the application payload only. It requires a
compatible .NET Desktop Runtime and must not be presented as total disk usage on
a machine where that shared runtime is not already installed.

Recreate the size comparison without retaining temporary publish output:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\measure-publish-size.ps1
```

Pass `-NoRestore` after the solution has already been restored. Use
`-OutputPath` to save a JSON summary.

## Runtime-efficiency update

The runtime-efficiency implementation was measured on 2026-10-07 by comparing
baseline commit `ebf85e8` with candidate commit `2322fa6`. Both were Release,
framework-dependent `win-x64` builds with isolated singleton names, isolated
settings, disabled app profiles, and the settings UI closed. Each build received
a 10-second warm-up followed by two 30-second passes sampled every 500 ms on the
same Windows 11 machine with 20 logical processors. The table averages the two
per-pass summaries.

| Metric | Baseline | Candidate | Change |
| --- | ---: | ---: | ---: |
| Working set average | 139.64 MB | 132.82 MB | -6.82 MB (-4.9%) |
| Working set P95 | 140.84 MB | 133.59 MB | -7.25 MB (-5.1%) |
| Private memory average | 81.72 MB | 76.16 MB | -5.56 MB (-6.8%) |
| Private memory P95 | 82.89 MB | 76.76 MB | -6.13 MB (-7.4%) |
| Whole-machine CPU average | 0.144% | 0.051% | -64.5% |
| Whole-machine CPU P95 | 0.530% | 0.153% | -71.2% |
| Handles average | 882 | 885.5 | +3.5 (+0.4%) |
| Threads average | 40 | 39.5 | -0.5 |

These absolute memory values are not directly comparable with the earlier
single-file sample because the distribution model differs. The controlled pair
is suitable for evaluating the change itself. A protected-window run, movement
run, reveal run, and long-duration stability run are still required before a
stable release.

The candidate's framework-dependent application payload was 0.309 MB across 6
files, below the 1 MB budget, with no new package or runtime dependencies.

An additional close-to-tray experiment tested destroying and recreating the WPF
settings window. One 30-second pass measured 168.20 MB working set and 111.85 MB
private memory for the reusable baseline window, versus 181.92 MB and 121.88 MB
when the candidate window was destroyed. Because immediate tray memory regressed
by 13.72 MB working set and 10.03 MB private memory, that change was reverted in
commit `6115549`. The final implementation retains the reusable hidden settings
window.

The retained implementation reduces work through candidate-only detailed
window inspection, cached process identity, reusable discovery and overlay
collections, cached effective zones, render invalidation for unchanged
overlays, and adaptive polling.

## Secure frosted-renderer measurement

The opaque rasterized frosted surface was measured on 2026-10-08 using the
installed 1.1.0 single-file build at commit `4b097ff` as the baseline and the
candidate single-file build at commit `4e48faf`. WhatsApp was visible and
stationary, both builds used the same settings at the equivalent visual
intensity, and the settings window remained closed. Each process received an
8-second warm-up followed by one 30-second pass sampled every 500 ms on the
same Windows machine with 20 logical processors.

| Metric | Baseline | Rasterized frost | Change |
| --- | ---: | ---: | ---: |
| Working set average | 296.80 MB | 298.24 MB | +1.44 MB (+0.5%) |
| Working set P95 | 296.82 MB | 298.27 MB | +1.45 MB (+0.5%) |
| Private memory average | 192.53 MB | 193.70 MB | +1.17 MB (+0.6%) |
| Private memory P95 | 192.62 MB | 193.80 MB | +1.18 MB (+0.6%) |
| Whole-machine CPU average | 0.062% | 0.085% | +0.023 percentage points |
| Whole-machine CPU P95 | 0.445% | 0.409% | -0.036 percentage points |
| Handles average | 970 | 973 | +3 (+0.3%) |
| Threads average | 38 | 38 | No change |

The candidate remains well below the 0.5% average CPU budget for a stationary
protected window. The small memory increase is the expected cost of retaining
an opaque 192x192 BGRA texture and its WPF composition resource. This is a
short directional comparison; movement, reveal, two-window, and long-duration
stability scenarios remain required before the next stable release.

## Live-blur replacement measurement

The tiled frosted renderer at product commit `48a0d79` was compared with the
live-blur candidate at product commit `4b8d691` on 2026-10-08. WhatsApp was
visible and stationary at 30% strength, the settings window remained closed,
and both single-file builds used the same local settings. Each build received
an 8-second warm-up followed by two 20-second passes sampled every 500 ms on
the same Windows machine with 20 logical processors. The table averages the
two per-pass summaries.

| Metric | Tiled frost | Live blur | Change |
| --- | ---: | ---: | ---: |
| Working set average | 286.20 MB | 284.39 MB | -1.81 MB (-0.6%) |
| Working set P95 | 290.04 MB | 284.78 MB | -5.26 MB (-1.8%) |
| Private memory average | 171.44 MB | 172.66 MB | +1.22 MB (+0.7%) |
| Private memory P95 | 174.65 MB | 173.02 MB | -1.63 MB (-0.9%) |
| Whole-machine CPU average | 0.216% | 0.116% | -0.100 percentage points (-46.3%) |
| Whole-machine CPU P95 | 2.356% | 0.729% | -1.627 percentage points (-69.1%) |
| Handles average | 951 | 945.5 | -5.5 (-0.6%) |
| Threads average | 39 | 37 | -2 (-5.1%) |

The candidate replaces repeated WPF tile composition with one bounded image
per protected window and reuses that image element between captures. Average
private memory is effectively neutral within short-run variance, while the
stationary-window CPU and tail working set improved in this comparison. The
result is directional rather than a release guarantee; movement, reveal,
two-window, and long-duration stability scenarios are still required.

## Performance budgets

The first optimization work should target these budgets on the reference
machine. A pull request may refine a budget when it includes measurements that
explain why the original target is unrealistic.

| Metric | Initial budget |
| --- | ---: |
| Working set with settings UI closed | 100 MB or less |
| Private memory with settings UI closed | 140 MB or less |
| Idle whole-machine CPU | 0.1% average or less |
| Two protected windows, stationary cursor | 0.5% average CPU or less |
| Working-set and handle growth over 8 hours | Less than 3% |
| Panic-mask response | 150 ms or less |
| Overlay response to move or resize | 100 ms or less |
| Framework-dependent application payload | Less than 1 MB |
| Self-contained single-file distribution | Less than 65 MB |

## Required measurement scenarios

Every performance-focused pull request should report the affected scenarios:

1. Tray idle with no supported app running.
2. One supported window visible and stationary.
3. WhatsApp and Telegram visible and stationary.
4. Continuous target-window movement and resize.
5. Continuous pointer movement through the hover-reveal region.
6. Pause and resume repeated 25 times.
7. Settings window opened and closed 25 times.
8. Eight-hour resource-stability run before a stable release.

Report the exact commit, binary version, operating system, logical processor
count, sample duration, and interval with every saved result.
