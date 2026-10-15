# Bootstrap/readiness prototype (phase 5b)

Status: **developer-only executable/IPC prototype**, not the production launcher.
App/Core/Windows still do not reference Updates. The installer, shortcuts, startup
entry, product version `2.0.0-beta.2` and settings schema 12 are unchanged. No
Update button, apply/recover/default-launch command or production key is enabled.
See [recovery kernel](update-recovery-kernel.md) and [delivery gates](in-app-updates.md).

## Executables and authority boundary

- `tools/PrivacyMask.Bootstrap.Prototype`: WinExe, no console window. Accepts
  **only** explicit `--probe <install-id> <operation-id>`. Derives the root from
  its own executable location; no arbitrary root, executable, URL or script args.
- `tests/PrivacyMask.Readiness.Probe`: separate fault-capable TEST peer. It is
  **not** PrivacyMask.App and does not load settings, tray, captures or masks.
- `tests/PrivacyMask.Bootstrap.SmokeChecks`: provisions a private owned Temp
  fixture, copies only the two published developer executables, runs the test
  matrix and emits a generated numeric/OS/build measurement report.

The host validates the phase-5a private managed marker/lease and operation path,
then reads a bounded source-generated **test** request. It rejects any active
recovery journal, even if valid, and cannot execute `PrivacyMask.App.exe` or run
RecoveryRestorer. It holds the lease until its exact owned peer has exited and
cleanup finishes. The test peer's fixed path, private ACL, size, SHA-256 and
special product identity are checked; a read handle pins its bytes while used.
These checks are fixture integrity, **not a release signature or apply consent**.

No process discovery/kill by name, process-tree termination, elevation, download,
registry/shortcut/startup mutation, settings restore or application replacement.
Default/unknown/`--apply` arguments fail without launching a peer. The only
process the prototype may terminate is the exact retained test process it
created. Do not distribute/copy this prototype as a production entrypoint.

## Process-bound readiness exchange

1. Create a fresh random, first-instance, asynchronous, current-user-only local
   named pipe. On Windows CurrentUserOnly also restricts elevation level. No
   server, network port, cookies or account is introduced.
2. Start the fixed test peer hidden, retain its Process/SafeHandle, and capture
   PID, UTC creation time and expected executable path. Use the retained handle
   with QueryFullProcessImageName, not module enumeration that may be incomplete
   during native loader startup. Reject changed PID/time/image and reparse paths.
3. After connection, get the actual client PID from the Windows pipe handle and
   match the retained live peer **before** disclosing a fresh 256-bit challenge.
   The client likewise checks the OS server PID, expected parent time and image
   before reading. Pipe name and IDs are not credentials; the challenge is sent
   only over the checked connection, never argv or logs.
4. Exchange fixed 70-byte frames: magic, protocol 1, explicit message kind,
   install/operation GUIDs and 32-byte challenge. No lengths, JSON, paths, PINs,
   settings, arbitrary commands or unbounded frame buffers. Require exact role,
   session and nonce, constant-time nonce comparison and complete bounded reads.
5. Recheck live peer identity after Ready, observe the test child for 100 ms,
   then send Accepted. The observation is a **fixture check**, not the planned
   real application's 60-second readiness qualification. Single-use state cannot
   be accepted before verified Ready, reused after cancellation/failure, or replayed.
6. The test child exits after Accepted. All IO/observation/exit waits have an
   operation deadline; finally cancels/observes pending exchanges, closes pipes
   and terminates only the owned test peer if necessary. No orphan service remains.

Accepted/Ready here mean **test protocol receipt only**, never install commit,
successful settings migration or working protection. The actual App must later
report after its own validated settings, singleton, shell/tray and protection
startup. The future worker must persist durable commit before displaying update
success, and must integrate consent/Panic/edits/process-exit/recovery gates.

Source-generated metadata now covers every supported update contract, not just
the recovery record. `ContractJson` keeps duplicate/unknown-field, strict UTF-8,
nullable/required-constructor and size bounds, and rejects unregistered types
instead of falling back to reflection. Context options are copied, not shared
mutable serializer state. This enables Native AOT without hiding trim/dynamic-code
warnings or weakening manifest authentication. Existing signed-package tests
must remain green, including exact signed-byte verification.

## Build and measurement

```powershell
pwsh -File scripts/test-bootstrap-prototype.ps1 -PublishMode SingleFile
pwsh -File scripts/test-bootstrap-prototype.ps1 -PublishMode NativeAot
```

Both build standalone Windows x64 developer executables with warnings treated as
errors, then run identical real-process cases. Native AOT requires the documented
Visual Studio C++/Windows toolchain; the script does not install it, silently
fallback, or claim an AOT result from a normal .NET executable. CI runs both modes
on Windows and prints the reports from ignored
`artifacts/bootstrap-prototype/<mode>/measurements.json`. The normal release ZIP
does not include either developer executable.

Record meanings:

- `helperBytes`/`peerBytes`: published executable size, not installed application
  size, compressed download size or temporary bundle-extraction/cache footprint.
- `processWallMs`: parent stopwatch from before starting the helper until its
  exit/output receipt; includes helper runtime loading, child startup, protocol,
  100 ms observation, acknowledgement and cleanup/scheduling overhead.
- `elapsedMs`: helper-managed stopwatch only; excludes its earlier native/CLR
  startup. Do not call it full cold-start time or compare it to app UI readiness.
- `helperPeakWorkingSetBytes`/`peerPeakWorkingSetBytes`: OS-reported peak resident
  working set while the respective process is alive. Includes runtime/page-cache
  effects; not a memory leak test or the steady-state App footprint.
- private bytes: helper end-of-probe and peer pre-ack snapshots, **not peaks**.
- `bundleCacheBytesAfterMatrix`: extracted runtime files remaining after this
  matrix, not peak disk usage. DOTNET_BUNDLE_EXTRACT_BASE_DIR confines extraction
  to the owned fixture, which is removed after testing; no per-build probe cache
  is deliberately left in the user's ordinary .net Temp area.
- OS/framework and embedded helper/peer build versions identify the sample.

SingleFile and NativeAot CI matrix jobs are independent VMs; timings are not a
controlled same-machine A/B benchmark. Local reports also use a different OS/SDK.
Bundle extraction/cache and warm/cold disk conditions are not controlled. Use
size and broad resource evidence for a provisional packaging choice, not a
production latency promise. Full recovery/helper references and real readiness
will change the eventual shipped binary size and workload.

### Recorded prototype sample

2026-10-15, Windows build 26100, .NET 10.0.13, PR checkout merge
`7da9cc43e8245f29ec0b8e739685d027134e33b4` (PR head `ff550f5`).
Raw bytes and milliseconds below are from the Ready case; metrics are not rounded
MB labels or a promise for the final helper/application.

| Metric | Self-contained single-file | Native AOT |
| --- | ---: | ---: |
| Helper executable bytes | 37,677,060 | 4,652,544 |
| Test peer executable bytes | 37,673,982 | 2,132,480 |
| Helper peak working set bytes | 76,554,240 | 13,160,448 |
| Peer peak working set bytes | 64,135,168 | 10,260,480 |
| Full fixture process wall ms | 508.5894 | 192.047 |
| Helper-managed protocol ms | 392.49 | 172.93 |

Evidence: [single-file job](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/actions/runs/38703014525/job/116315516972),
[Native AOT job](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/actions/runs/38703014525/job/116315517229).

Provisional choice: Native AOT for the future Windows x64 helper, based on the
much smaller prototype binary/resident footprint. It is not added to the release
package yet. Re-measure with signed apply/recovery/migration and the actual App
readiness workload linked, qualify Windows 10/11 standard-user execution, and
measure extraction/cache/disk and cold/warm starts before freezing the choice.

## Verification and remaining gates

Unit cases cover malformed headers/roles, ID/nonce replay, frame limits, truncated
reads, cancellation, native process time/path checks, no challenge to wrong PID,
single-use/premature acknowledgement and byte-compatible strict source generation.
The published-process matrix covers Ready, wrong operation/challenge, disconnect,
stall/deadline, unexpected PID (a client capable of answering if disclosed), wrong
parent creation time and early exit. Unsupported/default CLI paths are also checked.
Every negative case must be non-Ready; no journals/app payloads may be created.

Remaining before phase 5 can complete: signed preparation/freshness, original
snapshot provenance, reserve space and terminal-record lifecycle; the production
launcher's forward apply/recovery/start paths; exact old-process graceful exit;
restricted real-App readiness and durable commit acknowledgement; installer,
shortcut/startup and uninstaller migration/repair; crash/power-loss and Windows
10/11 standard-user qualification. No prototype test proves actual App readiness,
settings/PIN migration, gapless masks or a successful real-release upgrade.

## Sources

- [CurrentUserOnly and first pipe instance](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0)
- [OS pipe client PID](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid)
- [Process image query](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)
- [Native AOT prerequisites and deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
