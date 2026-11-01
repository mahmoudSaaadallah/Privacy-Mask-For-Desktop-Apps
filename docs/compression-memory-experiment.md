# Compression, memory and footprint experiment — 2026-11-01

## Owner observations and scope

The owner ran the full-App probes in their local PowerShell because the agent's
installed-file view disagreed with the owner's UI and process metadata. The
owner-generated packets consistently identify **2.0.0-beta.17**, source
`549e7daceee28cb02e0fe695ea0436548660e7cf`, binary SHA-256
`00706289505b409246a532e71ee24dcf8d1ed34b5b15cf879f73bc97f63ff3ac`.
The App was not replaced/reconfigured/stopped by this experiment.

- Post-use, with UI closed/no protected window visible as reported by the owner:
  working-set mean 330.5624 MiB, private mean 202.2851 MiB, CPU 0.03163%.
- Fresh minimized process, same binary, three 60-second passes after a 30-second
  warm-up: working set 285.7462 MiB, private 163.5618 MiB, CPU 0.0852%.
- Later numeric runtime diagnostic on another process of the same binary:
  working set 37.0892 MiB; private snapshots **228.7578/231.7891 MiB**;
  managed heap estimate mean 5.7304 MiB, last-GC committed 10.4883 MiB.
  These do not imply a code improvement, zero unmanaged cost, or no leaks.
- A 13.384 ms query-only region snapshot on that process reported private
  executable mappings 81.0391 MiB, private non-executable 133.3984 MiB,
  image mappings 132.4609 MiB and data mappings 264.9688 MiB. Private snapshots
  stayed 228.6758 MiB; working set stayed 31.7188 MiB during that query.

Do **not** add these virtual mapping sizes and label them physical RAM. Working
set is residency, private usage is commit charge, and mapping type is not an
allocator identity. Copy-on-write image/data pages can become private while
retaining their mapping type. No exact native heap size follows from subtracting
GC committed snapshots from process private usage. Historical GC observations
can be stale. The snapshots neither identify an allocation stack nor prove leaks.

Local ignored evidence folders:
`full-app-current-state-20261029-101204-e39fdfcf`,
`full-app-FreshTrayIdle-20261029-104200-d33efe84`,
`app-memory-counters-20261101-100856-90d03ca2`, and
`app-memory-regions-20261101-102452-5f1b8050`, all under `artifacts/`.
No settings, PINs, profile/window titles or capture contents were collected.

## Hypothesis and unchanged release behavior

The current production profile has `EnableCompressionInSingleFile=true`.
Microsoft documents that compressed bundled assemblies are decompressed into
memory on load. This is a plausible contributor to private/executable memory,
not proof that all 81 MiB belongs to compression or JIT code.

This delivery adds an isolated comparison tool and correct build metadata for
the existing GUI fixture. It **does not change the App publish profile**,
renderer, defaults, update policy/trust/schema/launcher protocol, installation,
product version (beta.17 / FileVersion 2.0.0.17) or resource budgets. No release,
tag, key or GitHub environment change is involved. A production choice needs
full-App evidence and an explicit disk/cache trade-off decision.

## Reproduce the developer experiment

Use Windows x64, PowerShell 7 and .NET 10 SDK, with a fresh evidence directory:

```powershell
pwsh -NoProfile -File scripts/measure-compression-baseline.ps1 `
  -OutputDirectory artifacts/compression-new-session
```

For separate build/measurement phases:

```powershell
pwsh -NoProfile -File scripts/measure-compression-baseline.ps1 `
  -OutputDirectory artifacts/compression-new-bundles -PrepareOnly
pwsh -NoProfile -File scripts/measure-compression-baseline.ps1 `
  -OutputDirectory artifacts/compression-new-measurement `
  -PreparedDirectory artifacts/compression-new-bundles
```

Prepared bundles must match the current commit, SDK, version and payload hashes.
Existing evidence is never overwritten. Preparation builds two self-contained
`win-x64` single-file versions of **PrivacyMask.Gui.Benchmark**, changing only
compression; ReadyToRun/trim/debug flags stay the same. The generated assembly
metadata emits actual SingleFile/SelfContained/Compression values; the previous
hard-coded FrameworkDependent label is no longer used for bundled fixtures.

The fixture loads the real UI/resources with 42 synthetic profiles and remains
hidden. Its separate Application class never runs App/Shell startup, protection,
capture, global hotkeys, user settings IO, startup registration or update HTTP.
These are **not full-App/tray/live-blur measurements**.

Each variant has three fresh-process passes, each with 30 seconds warm-up then
60 seconds sampling at 500 ms. Order alternates **C,U / U,C / C,U**. Only owned
fixture process objects are controlled/closed; no user App is stopped. A bounded
region query follows each pass. The metadata helper opens QUERY_INFORMATION
only (0x0400), not VM_READ/VM_WRITE/VM_OPERATION; it exports numeric aggregates,
not memory bytes, addresses or module paths. Queries are best effort/non-atomic.

Native bundle extraction goes into an experiment-owned cache per variant.
Record **EXE**, complete fixture payload, **ZIP** and native extraction-cache
bytes separately. These are logical bytes for the fixture, not release/install
footprint, user state, filesystem allocation or a shared-framework requirement.
In particular a similar ZIP can hide a much larger installed EXE. Ready timing
includes uncontrolled cache/OS conditions and is not a cold-start guarantee.

Admission, raw strict runtime summaries and per-pass region metadata are retained
if a later case fails. Only a complete set with normal child exits gets the
aggregate summary. No forced GC, working-set trimming, protection-quality or
sampling-cadence reduction is used. Do not run builds/tests during the resource
passes; ordinary unrelated desktop workloads are still a source of noise.

## Results and delivery validation

The complete experiment ran from source
`27decc76be182743d22ac9334832ade608927f2e`, SDK **10.0.402**, self-contained
Core/Desktop runtime **10.0.13** for both variants. Preparation and measurement
metadata report a clean tree at admission. The four managed product assemblies
(App/Core/Windows/Updates) were additionally checked byte-identical across the
two builds; local evidence is `compression-bundles-20261101/common-product-assembly-hashes.json`.
The App DLL SHA-256 is
`7861ac45cc2b1c6d06485b340bc1f64546df684661c0f35f0a4f3104cf782cff`.
The fixture's own generated deployment metadata intentionally differs between
variants so its distribution declaration is truthful.

All **six** passes completed, 60.219–60.371 seconds each, 117 samples/pass,
minimum coverage **0.969**, maximum gap **564.1 ms**. No build/test workload ran
during those resource passes. Means of three per-pass averages:

| Synthetic hidden GUI metric | Compressed | Uncompressed |
| --- | ---: | ---: |
| Working set MiB | 199.797 | 86.450 |
| Private memory MiB | 102.717 | 40.407 |
| Whole-machine CPU % | 0.0207 | 0.0167 |
| Handles | 616.0 | 616.0 |
| Private executable committed mappings MiB | 60.160 | 0.008 |
| EXE bytes | 75,782,777 | 173,888,200 |
| Complete fixture payload bytes | 75,783,306 | 173,888,729 |
| Fixture ZIP bytes | 70,206,220 | 71,830,068 |
| Separate native extraction cache bytes | 8,215,912 | 8,215,912 |

This reproduces a substantial packaging-related difference in this fixture:
private memory is **62.310 MiB** lower (about 60.7%) without bundle compression,
and the private executable region difference is about 60.152 MiB. This is **not
an achieved 62 MiB reduction in the installed full App**, nor attribution of all
81 MiB observed in its region snapshot. Full-App/Shell/protection workloads were
not part of this experiment, and no production optimization was applied.

Working set was noisier: uncompressed per-pass means were 98.96, 62.31 and
98.08 MiB; private means were much tighter at 40.42, 40.33 and 40.47 MiB.
Do not treat the working-set average as a guaranteed saving or a controlled
memory-pressure experiment. The private-memory contrast is repeatable here;
actual App use, response latency, startup and eight-hour stability remain open.

The trade-off is concrete: fixture EXE/payload grows **93.561 MiB**, even though
the ZIP grows only **1.549 MiB**. Payload plus observed native extraction cache
is about **80.108 vs 173.669 MiB** in logical bytes. These are fixture totals,
not the full release/installed-footprint figures. Production profile stays
compressed pending matched full-App tests and the owner's disk/RAM decision.

Local complete evidence: `artifacts/compression-baseline-20261101/summary.json`
and `artifacts/compression-bundles-20261101/prepared.json`. Fixture EXE hashes:
compressed `8b95366e139acfad0e757edc84ebe011334841abeb9368b7da29f0eba66665b6`;
uncompressed `16a8d5dc2d3481dc2c5af2ce4ac6741f2b343499bb4a1dfb68ece6c49dcf8071`.

Local delivery validation passed: **925** unit tests; zero-warning/error solution
build; **48** PowerShell syntax checks; existing policy/installer/rollback/compiled
GUI fixtures; updated framework-dependent GUI metadata checks; and **21** new
compression/region/identity/cleanup checks. These include rejecting different
source and runtime configurations. CI runs the short synthetic smoke, not the
nine-minute resource baseline or noisy resource thresholds.

The two-second smoke is tool coverage, not a resource-budget assertion. Stable,
eight-hour, real input/PIN/DPI/Sleep and live recovery gates remain open; the
owner's beta.14-to-beta.15 trial remains reported successful update evidence,
not universal recovery qualification. No budget is relaxed or user installation
changed by this delivery.

## Primary references

- [Microsoft single-file compression behavior](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/)
- [Process working set and private commit fields](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex)
- [VirtualQueryEx and copy-on-write classification](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex)
