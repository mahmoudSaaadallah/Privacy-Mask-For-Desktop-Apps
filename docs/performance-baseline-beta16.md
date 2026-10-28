# Beta.16 performance baseline tooling — 2026-10-28

## Scope and version

PR #64 is merged at c0f181b841217838b9b4793476782445d52c2ec1. This follow-up
changes developer measurement tools/tests only, not production App code,
defaults, capture quality, polling, signing, schemas or update policy. Product
version remains 2.0.0-beta.16 / FileVersion 2.0.0.16. A later measured production
optimization will require its own Preview increment; no release is published here.

The owner's running local App was beta.7. It was not stopped, reconfigured or
used as the current-version baseline. The owner chose a live synthetic-content
blur session on their device **later**; no present permission to interrupt their
protection or modify their installation is inferred.

## Reproduce the isolated GUI baseline

From a clean source checkout with developer PowerShell 7 and .NET 10 SDK:

```powershell
pwsh -NoProfile -File scripts/measure-gui-baseline.ps1 `
  -OutputDirectory artifacts/gui-baseline-01
```

The directory must be new. The runner builds an isolated Release,
framework-dependent benchmark, waits for READY, binds the process ID plus start
time and validates its compiled product version. Overview, Applications and
Settings each run in a fresh owned process with 42 synthetic profiles at
1280x800 DIP. Each receives 30 seconds of warm-up and three consecutive 60-second
passes at nominal 500 ms sampling. Between pages the process exits normally via
an explicit control pipe. No visible window, production Shell, capture, network,
startup registration, settings IO or hotkey registration is started.

The harness pumps the dispatcher at 100 ms and retains one input-control worker;
it measures a hidden synthetic WPF UI, **not tray idle or the live renderer**.
Do not compare it directly with a self-contained installed App or historical
different-SDK/harness measurements. All three passes and their mean-of-pass
averages remain in JSON; averages are not global pooled P95s. SDK, source SHA,
dirty-tree flag, product identity/hash, OS version, logical CPU count, actual
sample count/duration, coverage, gaps and normalization are recorded.

The strict probe refuses process replacement, exit during warm-up, early exit,
intervals longer than the requested duration, coverage below 80% or a sample gap
above max(1000 ms, five intervals). CPU is percent of total logical-processor
capacity; the older *Mb field names retain binary MiB units. The ordinary probe
keeps its legacy partial-sample behavior unless RequireFullDuration is requested;
new Completed/coverage fields identify whether the pass qualifies.

```powershell
pwsh -NoProfile -File scripts/measure-runtime.ps1 `
  -TargetProcessId <agreed-test-process-id> -WarmupSeconds 30 `
  -DurationSeconds 60 -SampleIntervalMilliseconds 500 -RequireFullDuration `
  -OutputPath artifacts/live-test-pass-1.json
```

Do not supply a random process or call this a controlled live-runtime scenario
without setting up that scenario. Never export settings, PINs, titles or captures.

## Distribution and installation footprint

Package smoke tests optionally emit a separate footprint report:

```powershell
pwsh -NoProfile -File scripts/test-release-package.ps1 `
  -ArchivePath artifacts/release/PrivacyMask-2.0.0-beta.16-win-x64.zip `
  -MeasurementOutputPath artifacts/fresh-install-footprint.json
```

This verifies and installs the package into its own temporary location with
SkipRestart, checks hashes/shortcuts, records sizes, then uninstalls and removes
only the owned fixture. It does not launch the App or modify the user's copy.
Existing report files are refused. EXE, launcher, ZIP, extracted package, fresh
installed files and shortcuts have separate **logical-byte** totals. No private
paths or file contents enter the report.

Fresh-install bytes are not NTFS allocated size or total lifecycle consumption:
first-run native extraction caches, personal settings, shared runtimes, staged
downloads and rollback history are explicitly excluded. Measuring those belongs
to the later agreed live session; a framework-dependent payload is not the size
of a standalone installation.

## Static lifecycle review — not a runtime pass

Fresh package measurement (same SDK, separately built after the GUI run):
App identity `2.0.0-beta.16+08e5b33da481a08c329358d553454f3f9305ba8f`.
This commit adds tool regressions only; production App source is unchanged.

| Distribution component | Logical bytes | MiB |
| --- | ---: | ---: |
| Download ZIP | 74,304,738 | 70.863 |
| App EXE | 75,802,883 | 72.291 |
| Native AOT launcher | 8,986,624 | 8.570 |
| Whole extracted package | 84,821,206 | 80.892 |
| Fresh installed files (3 files, before first launch) | 84,789,623 | 80.862 |
| Two temporary fixture shortcuts, separate | 6,506 | 0.006 |

App SHA-256: `35dbba15d0bab1523874c839c4600fbe495243227d1c2d6968cfad586943f1c2`.
ZIP SHA-256: `3582638a01f6869bcb82ef1237abe077441fc13c99756eefe8854bfe7dd81e54`.
Local evidence: `artifacts/performance-package-20261028/footprint.json`.
The historical 65 MiB single-App budget is **not met**; it was not relaxed.
This is not a smaller package claim or total on-disk usage after real operation.

### Inspection findings

- Capture bitmaps/Graphics are scoped with using; HDC release and HBITMAP deletion
  are guarded by finally. Full-resolution source allocation remains a possible
  hotspot to profile, not justification to change rendering now.
- Overlay removal hides/closes obsolete windows and removes their dictionary
  entries. Hide clears canvas/images and resets the frame/capture lifecycle.
  Closed resets capture state; pending native-capture lifetime still needs live
  measurement under movement/close/reopen.
- Application icons already have a 64-entry bound; profile zone cache is weak-keyed.
  Existing bounded caches were not replaced. Shell disposal removes refresh and
  network subscriptions and drains owned update work.
- No leak, CPU reduction or stable budget pass is claimed from static inspection.
  No forced GC, quality reduction or sampling-policy relaxation was introduced.

## Evidence and next gates

The full three-page run completed on 2026-10-28. Windows 11 Pro 10.0.26300,
Intel Core i9-7900X, 20 logical processors, SDK 10.0.402; installed .NET 10
Core/Desktop runtime 10.0.13. Measurement source was
`c84ad6a3fe59e632d8360a08234393fe4e2dfdbf`, with a clean working tree at admission.
All pages loaded product `2.0.0-beta.16+c84ad6a3fe59e632d8360a08234393fe4e2dfdbf`;
App DLL SHA-256 was `e7d891eb0d5b06a771de6d8cb44713304a746b378705a0041170dde36f57df9f`.

Means of the three per-pass averages (not pooled percentiles):

| Hidden synthetic page | Working set MiB | Private MiB | Whole-machine CPU % | Handles |
| --- | ---: | ---: | ---: | ---: |
| Overview | 95.95 | 39.43 | 0.020 | 550.7 |
| Applications | 95.24 | 39.30 | 0.013 | 544.7 |
| Settings | 97.32 | 41.32 | 0.010 | 543.0 |

All nine passes were complete, 60.004–60.431 seconds each, with 116–117 samples.
Minimum coverage was 0.9666 and the largest observed sampling gap was 547 ms.
Raw local evidence is under `artifacts/performance-baseline-20261028-full`;
the ignored raw JSON includes process counters/identities, not settings or
captures. Normal desktop workloads were not fully controlled; builds/tests were
not run during the measured passes. These are fresh directional reference
values, not a paired improvement, leak proof or full-App budget pass. Short
two-second regression probes only verify the tools, not performance.

Validation passed: scripts/verify.ps1 (921 unit tests, zero-warning/error build,
38-script syntax checks, existing policy/installer/GUI fixtures), 12 owned-process
probe/identity/interval/overwrite checks, all three short isolated GUI runner
scenarios plus invalid-argument/timer-only/overwrite regressions, and a fresh
self-contained App/Native AOT package with isolated install/uninstall footprint.
CI runs these short regressions; the ten-minute measurement is a manual developer
experiment, not a flaky resource-budget assertion in CI. No production private
key, release, real-App execution or user installation was used by these tests.

Live tray/one/two-window blur, movement/resize, Hover/Alt, 25 real Settings cycles,
25 pause/resume cycles, real monitor/Sleep/PIN paths and eight-hour stability
remain **NotRun**. The owner chose to schedule the live device session later.
Do not relax the existing resource budgets or qualify Stable from this PR.

Follow-up: the owner's later synthetic-window session and bounded movement fix
are recorded in [the beta.17 experiment](live-renderer-beta17.md). It exercises
real capture/overlays on owned test windows; it does not retrospectively turn
this hidden-GUI baseline into full-App or third-party qualification.
