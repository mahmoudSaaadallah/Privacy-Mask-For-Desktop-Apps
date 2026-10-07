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
