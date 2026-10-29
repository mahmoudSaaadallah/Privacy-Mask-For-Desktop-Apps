# Full-App resource observation and eight-hour runbook

## Scope and current status — 2026-10-29

This delivery adds developer observation/report tools, not an App change. Source
stays **2.0.0-beta.17 / FileVersion 2.0.0.17**. PR #66 was merged as `7387f15`.
The device's running managed App was **beta.7** at preflight; it was not stopped,
updated, reconfigured or used as candidate qualification. The actual eight-hour
candidate session remains **NotRun**. No release or GitHub setting is changed.

The beta.17 owned-window experiment measures real overlays in a synthetic target
process. This observer instead binds to the complete existing PrivacyMask.App
process, including its normal Shell/discovery/protection/update work. It does
not launch, install, terminate, pause, click or reconfigure that process.
An isolated synthetic-process escape hatch is explicitly labelled in evidence
and cannot set `EightHourObservationCompleted`.

PowerShell 7 is needed for these developer tools only. The ordinary installer
and existing `verify.ps1` entrypoint keep their existing requirements. CI runs
short synthetic tool tests, never an eight-hour desktop test.

## 1. Prepare the agreed device session

- First finish the same-machine short full-App baseline, using the existing
  30-second warm-up / three 60-second passes at 500 ms. Record the package type,
  SDK/runtime, binary identity, fixed blur settings and test-window conditions.
  A different scenario or synthetic renderer process is not a paired baseline.
- Use a reviewed beta.17 build/package and normal installation/shortcut. Verify
  the source version before closing an older protection instance; do not let
  the observer choose a version from whatever process happens to be running.
  Installing/restarting the App is an owner-controlled separate action.
- Arrange an unlocked eight-hour period, synthetic/non-sensitive target content,
  stable monitor/DPI/power conditions, and enough disk space for CSV and report.
  Avoid builds or other unusually heavy tasks while measuring. Record unrelated
  activity when interpreting results; no machine-wide performance claim follows.
- Keep production blur quality, hotkeys, Alt 500 ms, Hover 600x70, startup and
  required updater policy unchanged. Never force GC or trim the working set.
- Do not disable updates to keep a run alive. An update restart ends this process
  observation; repeat the full session on the new candidate rather than joining
  process lifetimes. Sleep/resume is a separate functional scenario.

## 2. Admit exactly the expected binary

Use a reviewed candidate EXE from the package/build that will be tested. Its
SHA-256 must match the installed App; a merely identical version label is not
enough. This does not require installing from the source output.

In PowerShell 7, from the repository root:

```powershell
# This file must be the intended reviewed candidate, not beta.7 or an arbitrary EXE.
$candidate = Get-Item -LiteralPath '.\desktop-app\windows\win-x64\single-file\PrivacyMask.App.exe'
if (($candidate.VersionInfo.ProductVersion -split '\+')[0] -cne '2.0.0-beta.17') {
    throw 'Build/select the reviewed beta.17 candidate first.'
}
$candidateHash = (Get-FileHash -LiteralPath $candidate.FullName -Algorithm SHA256).Hash
$instances = @(Get-Process -Name PrivacyMask.App -ErrorAction SilentlyContinue)
if ($instances.Count -ne 1) { throw 'Start exactly one intended full App instance first.' }
$app = $instances[0]
$session = Join-Path $PWD ('artifacts\full-app-soak-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$observation = @{
    TargetProcessId = $app.Id
    ExpectedStartTimeUtc = $app.StartTime.ToUniversalTime()
    ExpectedProductVersion = $candidate.VersionInfo.ProductVersion
    ExpectedBinarySha256 = $candidateHash
    OutputDirectory = $session
}
.\scripts\measure-app-soak.ps1 @observation
```

Version/start/hash/name mismatch rejects admission **before creating output**.
Existing evidence directories are rejected. The collector never follows a new
PID after exit/restart and never kills the target. Its defaults are 30 seconds
warm-up, **28,800 seconds sampling**, and **5,000 ms** sample cadence. This is
the observer cadence, not a change to the renderer's refresh interval.

Keep the terminal/process open. In a second PowerShell terminal set `$session`
to the exact evidence folder printed/selected for this run. To stop observation
without stopping PrivacyMask:

```powershell
New-Item -ItemType File -Path (Join-Path $session 'stop.request')
```

Normal stop, counter failure, process exit, a large sample gap/suspend or clock
discontinuity is recorded as incomplete. A forced termination/power loss can
leave CSV/admission with no final summary; that is incomplete evidence too.
Do not concatenate runs, relabel a short run, or invent a final summary.

## 3. Perform and record real checks

Use quiet initial/final five-minute periods with the **same** window/protection
conditions. Between them exercise tray idle, one/two protected windows, movement
and resize, Hover and exclusive Alt, PIN success/error/cancel, plus **25 actual
settings open/close cycles and 25 Pause/Resume cycles**. Check the current
shortcuts and Alt held for 500 ms, including Alt combined with another shortcut
not revealing content. Do not record real PINs or personal content.

Record each result when it actually happens, not in a loop to manufacture checks:

```powershell
.\scripts\record-soak-checkpoint.ps1 -SessionDirectory $session -Check Hover -Result Pass
.\scripts\record-soak-checkpoint.ps1 -SessionDirectory $session -Check SettingsCycles -Result Pass -Cycles 25
```

Available checks: `TrayIdle`, `OneWindow`, `TwoWindows`, `MovementResize`,
`Hover`, `AltExclusive`, `PanicPin`, `SettingsCycles`, `PauseCycles`,
`MatchingEndpoints`. Results: `Pass`, `Fail`, `NotRun`. A passing cycle entry
requires at least 25 completed cycles. Events are append-only **manual operator
reports**, not automation, timestamps of sample phases, or proof of response
latency. A later Pass does not erase Fail. Missing checks remain unrecorded.
Quantitative 100 ms movement/150 ms Panic latency needs separate instrumentation;
visual impressions and five-second counters cannot prove those budgets.

## 4. Inspect evidence and export the report

```powershell
.\scripts\export-soak-report.ps1 -SessionDirectory $session
```

Open `report.html` from the evidence folder. It contains four offline static SVG
charts (working set, private memory, whole-machine CPU, handles), identity,
completion status, endpoint growth and all checkpoint history. It has no JS,
remote fonts/images or network content. Metadata is HTML-encoded. Graph buckets
retain extrema; `samples.csv` keeps every recorded sample. A new report filename
is required to regenerate; existing reports/evidence are not overwritten.

- `admission.json`: exact PID/start/version/binary hash, environment, protocol.
- `samples.csv`: incrementally flushed numeric counters and UTC timestamps.
- `summary.json`: completion/coverage/gap diagnostics, mean/max counters and
  first/last-window growth. No final summary means incomplete observation.
- `checkpoints.jsonl`: optional explicit operator reports, no free-form PIN/name
  or window-title fields. No settings, captures or profile data are read/exported.

Collector memory is bounded by the endpoint window rather than session length;
the last five minutes are kept in a queue while the full stream goes to CSV.
Report generation reads the bounded eight-hour CSV after the run. It does not
run inside the observed App. Disk/cache/runtime/rollback footprint remains a
separate package/runtime measurement.

## 5. Review; never infer Stable from the timer

`MeasurementStatus=Completed` means only the requested continuous observation
completed with the same live process, sufficient samples (coverage >=80%, maximum
gap <=5 intervals or 1 second, whichever is larger), and no detected interruption.
`EightHourObservationCompleted=true` additionally requires all 28,800 seconds on
a non-synthetic full App. **QualificationStatus always remains NotEvaluated.**

The report checks completion against admission, CSV count and duration. It does
not automatically approve even a complete eight-hour run. Review crashes,
hangs, accidental reveals, missing/failed functional checks, ordinary updater
behavior, endpoint conditions and resource trend. Fixed sampling cannot detect
every transient reveal, short hang or delay; functional review remains required.

The historical limits in [performance-baseline.md](performance-baseline.md)
remain unchanged: working-set/handle growth **less than 3% over eight hours**,
100 MiB working set / 140 MiB private with UI closed, 0.1% idle CPU and 0.5%
stationary two-window CPU, plus their response/package limits. Endpoint means
are diagnostics, not permission to ignore transient peaks, differing workloads
or a leak between endpoints. Compare like conditions; do not automatically
relax limits when existing builds miss them. Per-scenario CPU budgets need
the short controlled passes, not a whole-session mixed-workload average.

Full-App baseline, eight-hour observation, actual keyboard/PIN/third-party/DPI,
Sleep/Resume and live recovery remain **NotRun** until their actual evidence is
collected and reviewed. The owner's beta.14-to-beta.15 trial remains a reported
successful update, not qualification for every recovery/device path.

## Tool delivery validation

On 2026-10-29, local checks passed: **42** owned-process observer checks,
**16** offline report/checkpoint checks, and Windows PowerShell syntax validation
for **46** scripts. The original Windows PowerShell `verify.ps1` also passed:
**925** unit tests, zero-warning/error solution build, policy fixtures,
transactional installer/rollback, installer entrypoints and compiled WPF GUI
fixtures. Those GUI scales are simulated, not real monitor/DPI qualification.

A read-only device preflight checked the running beta.7 against the existing
reviewed beta.17 artifact. It was rejected at version admission, created no
evidence directory and left the original process running. No settings were read
or changed, no candidate installed, and no eight-hour pass is claimed. New CI
checks use synthetic seconds only; mathematical 28,800-second fixtures are logic
checks, not elapsed real-device testing.
