# GUI qualification — 2026-10-13

The sidebar implementation is complete, but stable-release qualification is
not complete. This report separates repeatable automated checks from outstanding
interactive and long-duration checks. Version remains unreleased 2.0.0.

## Automated checks

- 196 unit tests pass; solution builds with zero warnings/errors.
- All 16 PowerShell scripts pass syntax checks.
- Transactional installer and isolated release-package smoke checks pass.
- The compiled WPF runner uses synthetic profiles and a separate Application
  class, not PrivacyMask's production startup. No registration, settings writes,
  protection runtime, capture, or third-party content is started.
- Six page layouts and profile details are exercised at DIP work areas
  equivalent to a 1920x1080 display at 100/125/150/200% scale.
- Tests verify selector binding, all ten advanced zone fields, no horizontal
  scrolling for primary profile controls, bounded dashboard references, search,
  selected-profile Save/Discard, invalid PIN feedback and clearing closed input.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-gui-smoke.ps1
```

Optional rasterized layout artifacts (not native-window screenshots):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-gui-smoke.ps1 -ScreenshotDirectory .\artifacts\gui-qualification
```

Simulating DIP sizes and raster DPI does not test actual per-monitor DPI changes,
screen-reader output, mouse hit testing or keyboard focus in a visible window.
The Windows Computer Use session failed initialization twice with
`failed to write kernel assets`; no live UI checks are claimed.

## Findings fixed

Long profile names and the advanced grid made the entire detail form scroll
horizontally at compact sizes. The main form now takes the viewport width;
the advanced DataGrid retains its own horizontal scrolling. Width/height labels
stay with their inputs as groups when wrapping. Navigation queues focus into
the selected page, and returning to Applications targets its search field.
Focus behavior is implemented but awaits live keyboard verification.

## GUI resource comparison

Windows 11 Pro 10.0.26300, 20 logical processors, .NET SDK 10.0.104.
Baseline product source: `5e079c9` (merged sidebar edit session, before
applications/dashboard redesign). Candidate: `308ed46`. These are NOT a
comparison against 1.x or a test of the real blur engine.

The same Release framework-dependent harness initialized Overview and
Applications with 42 synthetic profiles at 1280x800 DIP, then retained Overview.
Neither window was shown; no production discovery, capture, startup or settings
IO ran. Each independent process received a six-second warm-up and one
25-second sample at 500 ms intervals (49 samples). The benchmark app's version
metadata was 2.0.0+308ed46 for both runs; baseline product code came from the
archived baseline source, not that harness metadata.

| Metric | Baseline | Candidate |
| --- | ---: | ---: |
| Average working set (MiB) | 130.02 | 91.54 |
| Average private memory (MiB) | 67.78 | 38.41 |
| Average whole-machine CPU | 0.030% | 0.018% |
| Average handles | 499 | 499 |
| Average threads | 15 | 15 |

The observed differences are -38.48 MiB working set and -29.37 MiB private
memory. This single short sample is directional, not a statistical guarantee,
a live-window benchmark, or evidence that eight-hour stability budgets pass.
No additional runtime optimization was justified from this short comparison.

The repeatable harness is `tests/PrivacyMask.Gui.Benchmark`. Build its normal
candidate with `dotnet build tests/PrivacyMask.Gui.Benchmark -c Release`;
start its generated executable and sample that exact process using
`scripts/measure-runtime.ps1 -TargetProcessId <pid>`. It exits after 45 seconds.
To compare another compatible source checkout, build the same project with
`-p:ProductRoot=<absolute-checkout-path>` and a separate `-o` output directory.
Always record the source revision, scenario and distribution model.

Single-file publishing with the same SDK/runtime/profile compared baseline
`5e079c9` with qualification candidate `fee5602`:

| Executable | Bytes | MiB |
| --- | ---: | ---: |
| Baseline | 75,653,493 | 72.149 |
| Candidate | 75,661,506 | 72.156 |

GUI changes added 8,013 bytes. The existing 65 MiB distribution target is still
not met; this report does not silently revise that budget.

## Remaining release gates

1. Live keyboard Tab/Shift+Tab, page focus, F1/help/Escape, modal focus return,
   screen-reader labels and announcements.
2. Real 100/125/150/200% scaling and moving windows between monitors.
3. Actual PIN setup/change/unlock success, wrong PIN, cancel and close; clipboard
   contention and external contact launch failures.
4. Live runtime measurements: tray idle, one/two protected apps, moving/resizing,
   hover/Alt reveal, 25 pause/resume cycles, and 25 settings open/close cycles.
5. Eight-hour resource stability, overlay/panic latency and distribution budget
   review before tagging a stable release.

Use synthetic non-sensitive application content for capture tests. Do not
publish screenshots of protected content or PIN input. No release has been
tagged or published by this qualification work.
