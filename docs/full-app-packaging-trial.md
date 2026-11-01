# Full-App bundle compression trial (C/U/C)

Status: **prepared, isolated package tests completed; live full-App comparison NotRun**.
Production bundle compression remains enabled. This work does not publish a
release, qualify Stable, start an eight-hour observation or change resource
budgets. The preceding [synthetic experiment](compression-memory-experiment.md)
is a reason to measure the real App, not a claimed saving in the real App.

## Scope and admission

Use the same Windows x64 computer, settings, protected-window state and power
conditions for all stages. The owner selected manual installation; preparation
and CI never install into the owner's profile or execute the full App.

The owner-run helper installs and starts a diagnostic build only with explicit
`-RunForCurrentUser`. Exit PrivacyMask yourself through its tray Exit menu before
**each** stage. Protection is absent between Exit and startup. Use test content
and keep settings closed and every protected window closed/minimized during
measurement. The helper records this state as **operator-declared**, not verified.
It neither reads window content nor claims to certify that state.

Stages are C1 (compressed), U1 (uncompressed), C2 (compressed again). Each stage
starts a fresh App minimized and observes the exact PID/start time, binary hash
and full product version: 30 seconds of warmup, then three 60-second passes with
500 ms sampling. Later passes continue the same process without another warmup.
Allow about four minutes per stage including installation/startup/reporting.
Do not run builds or other benchmarks during resource observation.

The helper refuses any running `PrivacyMask.App`, holds the App's existing
singleton during replacement, and asks the installer to `-RequireStopped`.
That optional installer flag refuses a running installed process before the
ordinary force-stop path; the default installer behavior stays unchanged.
Existing settings/history are preserved through the normal installer transaction.
No process is killed, minimized or automatically restored by the trial helper.

Unknown installed versions, a different product version (ignoring Git build
metadata), or a higher installed FileVersion are rejected. This is a same-version
experiment, not an upgrade/downgrade tool; even a Stable build with a numerically
lower file revision cannot be replaced by this Preview pair.
Required updating remains active. If it installs a newer release, the PID/hash
admission fails and this old pair must not be resumed or used to downgrade.
An interrupted U1 can leave the uncompressed diagnostic installed; the run packet
records whether installation completed and prints an explicit warning. C2 is a
separate owner-run action, never a silent rollback.

## Prepare a new matched pair

From a clean reviewed checkout in PowerShell 7.4+:

```powershell
.\scripts\prepare-full-app-packaging-trial.ps1 `
  -OutputDirectory .\artifacts\full-app-packaging-trial-new
```

This needs the .NET SDK, Windows x64 Native AOT C++ linker prerequisites and free
space for two publishes, build intermediates, packages, installation staging and
rollback. It uses fresh output paths and never overwrites earlier evidence.
Native AOT launcher failure stops preparation with no fallback or installation.

The real App is self-contained/single-file, untrimmed and not ReadyToRun in both
variants. Only `EnableCompressionInSingleFile` differs. The common Native AOT
launcher is published once. Preparation checks byte-identical App/Core/Windows/
Updates managed assemblies and identical runtime configurations. `trial.json`
records Git source, dirty state, SDK, runtime, PE versions, file sizes and hashes.
The helpers recheck package contents, hashes and versions before using them.
These are local diagnostic identities, not a new signed update manifest format
or a replacement for release signature verification.

Each variant contains a standard-name ZIP/checksum and a `package` directory.
Use the helper, not a source-checkout BAT that could rebuild the default package.
Both variants deliberately retain the same Preview/FileVersion: this is a
packaging experiment, not a product release. **Do not publish these same-version
diagnostic archives as update assets.** The source identity can differ from the
previously installed beta.17; C1 makes a matched baseline before U1.

## Owner-run steps

The prepared local pair on 2026-11-01 is
`artifacts/full-app-packaging-trial-20261101`. After pulling the reviewed tools,
use that existing packet, or prepare a fresh pair as above and adjust `$trial`.
The archives and measurement artifacts are ignored local files, not committed
or uploaded by this change.

```powershell
$trial = '.\artifacts\full-app-packaging-trial-20261101'
# First exit PrivacyMask from its tray menu yourself.
.\scripts\invoke-full-app-packaging-step.ps1 `
  -TrialDirectory $trial -Stage C1 -RunForCurrentUser
```

Keep the printed evidence directory. Once C1 completes, exit the App again,
then run U1 with identical conditions:

```powershell
.\scripts\invoke-full-app-packaging-step.ps1 `
  -TrialDirectory $trial -Stage U1 -RunForCurrentUser
```

After U1, exit the App again and explicitly restore compressed diagnostic
packaging and repeat the baseline:

```powershell
.\scripts\invoke-full-app-packaging-step.ps1 `
  -TrialDirectory $trial -Stage C2 -RunForCurrentUser
```

C2 restores the paired **compressed diagnostic**, not the original released
binary byte-for-byte. The normal updater/installer remains the route back to
published builds. Never replace a newer installed release with this older pair.
There is no need to delete settings or uninstall between stages.

Each output contains `run.json`, three `pass-N` directories (admission metadata,
numeric CSV, summary, offline HTML), and query-only memory-region aggregates.
Neither addresses nor memory contents are recorded. Working set is resident
memory; private commit and committed mapping sizes are separate measures.
Region categories do not identify their allocator or prove a leak.

Use the three actual directories printed by the helpers:

```powershell
.\scripts\compare-full-app-packaging-trial.ps1 -TrialDirectory $trial `
  -CompressedFirstDirectory '<C1 evidence directory>' `
  -UncompressedDirectory '<U1 evidence directory>' `
  -CompressedRestoredDirectory '<C2 evidence directory>' `
  -OutputPath '.\artifacts\full-app-packaging-comparison-new.json'
```

The offline comparison rejects mismatched trial manifests, hashes, stage order,
overlapping stages, OS/processor counts, process identity, incomplete passes,
sample gaps, clock discontinuities and means inconsistent with raw samples.
Review all three stages separately as well as the six compressed-pass versus
three uncompressed-pass averages. Confirm the physical computer/settings/state
were the same; OS/processor metadata cannot prove this. A reproducible saving
must also survive live blur/response/startup checks before a packaging decision.

## Prepared packet and isolated evidence, 2026-11-01

Source `beebdd341bce5e8a8828c3db2c94d39c6554bf4f`, clean at preparation;
SDK 10.0.402; bundled Core/Desktop runtime 10.0.13; product `2.0.0-beta.17`,
FileVersion `2.0.0.17`. Trial ID `6e92de0aa8194ac0b3d6ace482554dc1`.
Both payloads share managed assemblies and launcher hash
`a80b89751eef66dcc30afca6a6b28fa5fe1035d9cc601b8fa4246430985b185b`.

| Logical bytes | Compressed | Uncompressed |
| --- | ---: | ---: |
| App EXE | 75,802,495 | 173,899,959 |
| Complete extracted package | 84,815,768 | 182,913,234 |
| ZIP | 74,302,199 | 75,931,443 |
| Fresh isolated installed files before App startup | 84,789,235 | 182,886,699 |

Uncompressed adds 98,097,464 bytes (93.553 MiB) to the EXE and 1,629,244 bytes
(1.554 MiB) to the ZIP. Installed figures exclude native extraction caches,
personal settings, update staging/rollback, shortcuts and filesystem allocation
overhead. Existing disk allocation or RAM use cannot be inferred from these sizes.

App hashes:

- Compressed: `8e48f6f565b95940ca5a213ae9f8a0108fa10d19d88302afcd787e1d03f5a8bf`.
- Uncompressed: `cf8d96d2c1308c744d9c71c19c2fd6264e6fd23fc15a6b46fbdf2e8cb6dbf2fc`.

Both ZIPs passed the existing isolated install/shortcut/hash/ACL/uninstall smoke,
without executing either App. Numeric footprints are in the packet's
`compressed-install-footprint.json` and `uncompressed-install-footprint.json`.
These checks establish package compatibility, not real App performance.
The uncompressed real EXE also passed **25** authenticated update-package checks
using a memory-only ephemeral test key, including download/staging and exact
baseline copies; no release or installed App was used. Local solution verification
passed **925** unit tests, zero build warnings/errors, **53** PowerShell syntax
checks, policy/installer/rollback/compiled GUI checks, and **20** additional
offline packaging-trial checks. Logs remain under ignored `artifacts`.

Offline fixture tests cover the opt-in gate, downgrade refusal, comparison math,
stage/environment/manifest mismatches and corrupt/incomplete raw evidence. CI
runs those short fixtures, not the owner-run installation/measurement steps.
The existing installer transaction test covers stopped-only refusal while an
owned fake App is running, followed by ordinary replacement/rollback behavior.

Live C1/U1/C2 RAM/CPU/handles, blur/input response, startup comparison and eight-hour
stability remain **NotRun/NotEvaluated**. No full-App saving is claimed and no
performance budget has been relaxed. This delivery does not change version
policy, update protocols/trust, settings schemas, renderer quality or defaults.
