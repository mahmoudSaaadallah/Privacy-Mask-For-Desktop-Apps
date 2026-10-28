# Preview A/B automatic-update experiment

The owner approved this smaller experiment after choosing normal use on a
friend's Windows 11 device instead of a VM. A is **2.0.0-beta.14**, with successful
automatic discovery every **10 minutes**. A was published after the reviewed
explicit-ID recovery described in [the publication incident](release-publication-incident.md).
B **2.0.0-beta.15** was separately approved and published on 2026-10-19 through
the protected workflow. It uses the normal **24-hour** cadence. Current source
prepares the beta.16 usability follow-up, not another publication.

## Current baseline evidence

The owner reported installing A on the friend's device. An initial screenshot
showed the unsupported-install message and no successful check; the owner later
confirmed that instance had been launched from the extracted folder. After using
the installed shortcut, a screenshot showed beta.14, Preview, UpToDate and a
successful check timestamp. This supports a working managed-install check, not
proof that that check was automatic or that an upgrade transaction has passed.

Subsequent owner-provided screenshots showed A discovering beta.15 and waiting
for visible protected windows. Paused Chrome/Codex/Cockpit profiles still had to
be closed/minimized. The owner then confirmed that Updates displayed beta.15
after the normal automatic flow. Record this as a **user-reported single-device
A-to-B transition**: no transaction receipt, post-update settings/PIN audit or
full OS/fault matrix was collected. It is not independent live qualification.

The sequence below is the original repeatable A/B protocol, not an instruction
to downgrade the friend's installed B or republish/reuse either tag. Future live
upgrade tests need a separately approved newer release. B and later use daily
successful checks with the same history/settings schemas. Beta.16 source does
not repeat the ten-minute exception.

## Scope and unchanged safeguards

- This binary enables Preview, never stable, activation. The runtime requires
  a Windows x64 process, OS build at least 22000 and the exact normal managed
  install/settings/launcher identity. Portable/source launches remain inactive.
  Build 22000 identifies the Windows 11 family; it is a technical filter, not
  evidence of qualification on that build or a recommendation to use an
  end-of-servicing OS. Use a serviced Windows installation. See
  [Microsoft's Windows 11 build history](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information).
- The protected tag-source gate permits only prereleases in this rollout.
  General version helpers still understand stable versions; enabling stable
  signing-job admission and stable runtime needs a separately reviewed change.
- The reviewed public P-256 identity stays `release-key-1`. No key is learned
  from GitHub metadata or CLI/environment overrides. No production private key
  is used by local tests or packaged for the friend.
- Versions/signatures/hashes, runtime/settings compatibility, same-version and
  downgrade rejection, authenticated handoff, safe restart and recovery remain.
  Unknown history, repair state or server Retry-After never becomes a forced retry.
- Only beta.14, optionally with ordinary build metadata, has the short cadence.
  It is not a settings option or general beta-channel exception. B and later
  default to daily using their own assembly identity, even with A's history.
  Failed requests still wait at least 15 minutes; server deadlines can wait longer.
- Panic, pending/invalid settings edits and visible protected windows postpone
  installation, not protection. Resolve normally and minimize/close protected
  windows. Never bypass PIN or silently save/discard edits to force the test.

## Simple execution sequence

1. Owner reviews and merges A's PR. Confirm green CI, clean exact merged source,
   protected environment/reviewer/bypass/tag policy and the public key-ID variable.
   The owner has reported successful direct environment-secret upload; checking
   its name/timestamp does not prove signing contents or a future signing pass.
2. After approval of exact tag `v2.0.0-beta.14` and its merged commit, push that
   tag through the protected release workflow. Owner reviews/approves the signing
   job under SoloMaintainer. The workflow builds/tests, signs in its isolated
   secret-bearing step, independently verifies and publishes the complete draft.
   Do not merge signing secrets into source or release assets.
3. Friend agrees to the Preview's automatic download/install/restart behavior,
   downloads A's complete normal installer ZIP from that release, extracts it
   fully and runs `Install-PrivacyMask.cmd` as their normal Windows user. No VM,
   GitHub account, SDK, C++ tools or PowerShell 7 is an end-user requirement.
   Existing beta.13 is not A: it first needs this one manual complete installer.
   Keep local settings backups private and do not send PINs or captures as evidence.
4. Launch A from its installed shortcut, check About, create a harmless test
   profile and let its first successful check finish. Confirm the 10-minute
   schedule is visible. Record only non-sensitive OS build/architecture, App
   version/source, installed launcher identity and normal check result.
   If the UI says to use a release installer, first check whether the running
   process is the extracted portable copy. Exit that copy normally and launch
   the installed shortcut. Never bypass managed-path/marker/ACL/OS checks to
   force a request; other unsupported-install causes still require diagnosis.
5. Prepare B in a separate PR, advancing product/file versions to beta.15/.15.
   The exact-version interval rule already makes B daily; do not broaden the
   beta.14 exception. Review/merge/test B and separately approve/publish exact
   tag `v2.0.0-beta.15` with its newer source and signed assets.
6. Leave A running and online. Do not press Check for updates for the automatic
   polling case. Within the next admitted check (normally up to 10 minutes after
   success, plus transfer time), observe B discovered/downloaded/verified. Server
   backoff, offline time or retained unknown/repair state can legitimately delay it.
7. Resolve normal privacy deferrals. With no Panic lock, edits or visible protected
   windows, observe automatic App replacement/restart without a confirmation.
   Confirm About says B, existing settings/profile behavior survives, normal PIN
   behavior remains and no repair is pending. Record transaction readiness/commit
   evidence locally without copying settings, PINs, personal paths or captures.
   A's stable launcher normally stays at A's version: protocol 1 updates the App
   only. A version label alone is not proof that the transaction fully committed.
8. B now uses 24 hours after successful checks. Its first request still respects
   retained attempt/server history. Ordinary Exit or network blocking remain
   possible Windows actions; this is not device management or anti-uninstall DRM.

## Limits of this first test

This is a normal, non-destructive A-to-B trial on a consenting user's device. A
separate Windows account/VM is recommended for fault/qualification work, not a
requirement for ordinary use or this basic consenting Preview trial. Do not fill
the disk, cut power, kill the host or tamper with their real settings/installation
to test failures. Those cases stay NotRun/Blocked until a disposable target and
separate authorization exist. A successful Windows 11 run does not qualify
Windows 10, all Windows 11 builds or the full stable recovery/fault matrix.

The original [qualification runbook](update-qualification-runbook.md) remains the
full stable-release gate, not a claim that the simpler experiment completed it.
Automated fixtures prove clock/state/security behavior with synthetic keys and
inputs, not an actual installed-App update. See [signing readiness](update-signing-readiness.md)
and [required policy](update-mandatory.md).
