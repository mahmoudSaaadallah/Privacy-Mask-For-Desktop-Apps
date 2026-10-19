# Update lab runbook — preparation, not a pass report

Use only disposable Windows 10 x64 and Windows 11 x64 VMs or separately approved
test machines, with a standard non-admin test account and synthetic app content.
Do not test on the developer's installed PrivacyMask, real protected content or
personal settings/PIN. This packet does not provision a VM or install anything.

## Before any live A-to-B execution

1. Record exact OS edition/build, architecture, actual DPI/monitor arrangement,
   VM/snapshot identity and standard-user status in that environment's evidence.
   Keep a clean snapshot and a baseline-A snapshot. Never infer Windows support
   from a CI runner or set the policy's minimum build from a fixture example.
2. Obtain separate owner approval for signing custody, public fingerprints,
   compiled App/launcher roots, bootstrap eligibility and publishing the exact
   A/B test releases. Beta.12 pins the owner's public root but retains a disabled
   runtime activation gate; current production runtime cannot perform live A-to-B.
   Offline fixture signing tests are not a bypass and do not qualify the real App.
   The planned SoloMaintainer policy requires the owner's manual release approval;
   it deliberately allows self-review and must not be described as independent
   review. See `docs/update-signing-readiness.md` in the source checkout for setup
   boundaries (the generated packet copies only this runbook).
3. Choose distinct reviewed versions A and B (B newer on the selected channel),
   exact merged source commits and immutable artifact SHA-256 values. Do not reuse
   a published version/tag. Record the installed launcher/bootstrap version/hash
   separately: protocol-v1 App updates do not upgrade the launcher or learn keys.
4. Copy only the approved installer release to the VM, verify its digest, install
   A from its whole extracted ZIP, and record A's App/launcher identity. This is
   the one-time manual bootstrap; .NET SDK/C++ build tools are not end-user inputs.
5. Configure synthetic profiles, non-sensitive settings and a disposable test PIN.
   Record presence and successful verification, never PIN digits, verifiers,
   capture frames, window titles, access tokens or private keys. Keep this evidence
   local; do not upload user settings/backups as PR or workflow artifacts.

## Execute and record

For each case, restore the appropriate snapshot and record actual steps, status
(NotRun / Pass / Fail / Blocked), sanitized evidence references and observed
App/launcher/settings outcome. A result applies only to that OS/build and exact
source/artifact pair. Record the tester/approver outside public evidence where
personal details are not necessary. Do not bulk change pending results to Pass.

For normal required A-to-B: use separately authorized activation-enabled builds
and observe startup/reconnect discovery on the build's fixed channel, automatic
verified download and no cancel/discard/confirmation. Keep synthetic protected
windows visible or Panic/edits active to verify deferral, then resolve them using
normal controls and close/minimize synthetic protected windows. Observe automatic
restart without a prompt. Confirm B's
exact identity, readiness/commit, saved preferences, profile behavior and normal
PIN operation. "Ready", a closed old window or a version label alone is not proof.

Failure cases must confirm A stayed alive or the exact previous App/settings were
recovered, not merely that an error appeared. Never delete unknown pending history,
staging or recovery backups to force a pass. Stop and preserve sanitized evidence
on a repair-required state. An abandoned prepared identity requires a new download;
safe privacy deferral before preparation can reuse its verified stage. In required
mode an ordinary Exit still drains safely; it is not a permanent in-app opt-out.

Actual disk-full, VM crash and physical power-loss are different tests. Do not fill
the host's real system drive or interrupt personal workloads. Physical fault tests
need their own explicit approval and disposable target; leave them Blocked/NotRun
until then. Repeated fixture crashes are not evidence for that gate.

## Safe preparation commands

From a clean committed source checkout, generate a fresh packet (no network,
keys, installation, capture, startup change, deletion or protected-App execution):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-update-qualification.ps1 -Name review-01
```

Existing offline developer checks can run without production secrets:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
pwsh -NoProfile -File scripts/test-update-transaction.ps1
pwsh -NoProfile -File scripts/test-update-manifest-schema.ps1
```

Record those results as automated fixture evidence only. Build/package validation
can additionally use the documented normal packaging and ephemeral real-EXE
signing scripts; they do not execute an installed real-App update.

## Approval and exit

Reconcile every pending case for both required OS targets. Failed/blocked/missing
cases cannot count as passed. Review resource budgets and archive only sanitized
reports, exact artifact/source hashes and approved public key fingerprints.
Owner release approval and key/environment setup remain separate from this unsigned
packet. No script consumes this packet as authority to sign, install or publish.
