# beta.19 Preview candidate

Beta.19 disables compression inside the App's self-contained single-file bundle.
The release ZIP remains compressed and the launcher remains Native AOT. This is
a deliberate memory/disk trade-off, with no renderer, settings, hotkey, reveal,
startup, update-policy, trust-key or protocol changes. Version is `2.0.0-beta.19`,
FileVersion `2.0.0.19`; AssemblyVersion stays `2.0.0.0`.

The earlier full-App and renderer comparisons observed roughly 75–81 MiB lower
private memory without compression in their measured workloads. This motivates
the Preview; it is not a guarantee for every device, a leak fix or a CPU speed
claim. See the [full-App report](full-app-packaging-results-beta18.md),
[one-window report](renderer-packaging-results-beta18.md) and
[motion/reveal report](renderer-motion-reveal-results-beta18.md).

## Focused validation — 2026-11-02

Candidate source: `f2ab43e97ba1a6f06c4bce3d076fda943ffcd804`, SDK 10.0.402;
self-contained App/Core/Desktop runtime 10.0.13, Windows 11 build 26300.

- 942 unit tests passed; 55 version-policy checks; 57 PowerShell files parsed.
- 26 renderer packet/recovery rejection checks and visible-fixture argument gates
  passed. These CI-safe checks do not open windows.
- Built the production uncompressed App and Native AOT launcher, created its ZIP
  and checksum, then verified an isolated fresh install, exact executable hashes,
  both Unicode shortcut targets/icons, version identity and uninstall cleanup.
- 25 signed-package checks passed with the actual candidate EXE and a disposable
  compressed older App baseline. Authenticated preparation/download/staging and
  retention were tested offline; the baseline App was not executed. Keys were
  ephemeral test keys, not the production signing key.
- 38 signed real-process replacement/recovery cases passed using owned test peers,
  including failed readiness, migration rollback and crash/handoff recovery. These
  are not an end-to-end production update on the owner's installation.

The owner-authorized visible recovery probe used identical managed assemblies
in compressed/uncompressed fixtures. Each warmed up four seconds and observed
24 seconds: three resize/settle cycles and three reveal/re-hide cycles per variant.
All twelve transitions completed within their two-second stable phase. No missing
mask sample or unfinished transition was recorded; opaque fallback was sampled
49 times in the compressed run and 46 times in the uncompressed run.

| Sampled recovery milliseconds | Compressed | Uncompressed |
| --- | --- | --- |
| First varied frame after observed settle | 3.317, 0.921, 0.769 | 1.313, 0.995, 0.917 |
| First varied frame after observed re-hide | 109.882, 109.098, 124.271 | 124.131, 110.421, 123.754 |

Settle timing starts at the first stable dispatcher tick: a valid frame may
already be available then. These small values are not time since the user's last
resize input. Coverage samples inspect three interior WPF geometry points,
excluding existing rounded corners. They do not establish pixel-level coverage
between ticks, visual fidelity, physical display latency or real Alt timing.
This short check is not a fresh CPU/memory baseline or Stable qualification.

The probe completed normally; all owned windows closed. Defender endpoints were
enabled in Normal mode, definitions 1.463.84.0; no matching PrivacyMask 1116/1117
events were found in the bounded session query. This does not classify the earlier
beta.17 report or prove continuous protection/universal antivirus clearance.

## Candidate footprint

| Component | Logical bytes |
| --- | ---: |
| App EXE | 173,900,983 |
| Native AOT launcher | 8,986,624 |
| Download ZIP | 75,934,816 |
| Extracted distribution | 182,920,402 |
| Fresh installed files before launch | 182,887,723 |
| Two shortcuts, separate | 6,506 |

The ZIP is about 72.4 MiB; App EXE about 165.8 MiB. This is approximately 94 MiB
more EXE space and 1.6 MiB more ZIP space than the earlier compressed beta.18
packet. Different source/version means this is a footprint reference; the matched
beta.18 experiments isolate the packaging effect. Native caches, settings,
staging, rollback copies and filesystem allocation overhead are additional.
The historical 65 MiB App budget remains unmet; it has not been relaxed.

App SHA-256: `a1ec7d722b80aaa980af9475ebcf3440618b73880a37a180e2d76a8c0075faf3`.
ZIP SHA-256: `ed380503c38422cea9dc7e4dfb2a2e7b93dbd92c331d03bf07d9697ea8516d98`.
These identify the local tested candidate, not a subsequently rebuilt release.

Ignored local evidence:

- `artifacts/beta19-candidate-20261102/` (ZIP/checksum/fresh-install footprint).
- `artifacts/beta19-recovery-pair-20261102/prepared.json` (matched test bundles).
- `artifacts/beta19-recovery-session-20261102-134037-d46d31ef/` (probe and security endpoints).

## Delivery boundary

Merge the reviewed PR, then obtain separate owner approval to publish
`v2.0.0-beta.19` using the existing protected Preview release workflow. No tag,
release, environment/secret change or personal installation was made by this PR.
The official workflow must build and sign the merged source; do not upload the
local test-key update assets or relabel an older published package.

Managed supported Preview installations retain mandatory startup/reconnect/daily
checks and normal safe-restart blockers. Existing settings are preserved; users
do not need to uninstall. A real production A-to-B update after publication is
still pending. Full keyboard/display checks, eight-hour resource observation and
remaining Stable gates are NotRun/NotEvaluated here and do not block handing off
this bounded Preview candidate. Stable is not approved by these results.

The old beta.18 C/U/C preparation helper intentionally requires a compressed
production baseline and now refuses the uncompressed default; do not use it to
restore compressed packaging over a newer installation.
