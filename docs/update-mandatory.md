# Required automatic updates — beta.13

Source: `2.0.0-beta.13`, FileVersion `2.0.0.13`. Settings/history schemas remain
13/1 and updater protocol remains 1. This implements the owner's required-update
policy with the explicitly agreed privacy deferrals. It does not publish a release
or enable the still-disabled compiled activation gate.

## End-user policy after separate signed activation

- Startup queues discovery once interactive startup/onboarding completes. A
  successful check is authoritative for scheduling for 24 hours, including across
  restarts. One timer schedules the next due check while the process stays running.
- Network availability/address changes queue coalesced dispatcher hints. Being
  online is not proof that GitHub is reachable; HTTPS results remain authoritative.
  There is no separate connectivity service, ping or third-party Internet probe.
- Daily scheduling uses the existing private bounded history. Failed requests
  retry no sooner than 15 minutes. Persisted GitHub Retry-After always takes
  precedence. Unknown/pending history is not erased to force a request.
- Stable builds follow Stable; beta/rc builds follow Preview. Channel filtering
  and semantic-version comparison remain unchanged: never downgrade, reinstall an
  equal version, learn a key from remote metadata or accept an unsigned fallback.
- Newer compatible signed candidates download automatically, with progress but no
  Cancel/Discard/manual-install choices. Required policy ignores legacy saved
  opt-out/channel choices; settings show locked controls. Public/backend cancel
  and discard requests cannot stop required downloads. Lifecycle shutdown still
  cancels/drains accepted work safely.
- Once verified, the session-owned stage is installed automatically without
  Save/Discard/restart dialogs. It waits instead while Panic is locked, the editor
  has pending/invalid changes, or an enabled-profile window is visible and not
  minimized. Users resolve those states through the normal controls; no data is
  silently saved/discarded and no PIN is bypassed. All matching enabled-profile
  windows are checked conservatively, including paused/focus-aware profiles.

## Safe handoff and recovery

The controller is single-flight. A retained safe stage checks restart readiness
every 10 seconds, not the network. An actual apply still performs the existing
fresh authenticated release checks and shared server-retry admission. Writers
freeze and drain before handoff; safety is checked again during handoff and just
before shutdown. Desktop-enumeration failure delays restart. A late unsafe state
after accepted preparation keeps masks/writers safe and requires normal repair,
rather than pretending an ambiguous prepared transaction can simply be retried.

Failed prepared identities require owned-stage retirement and a fresh signed
check/download. This refresh may bypass the daily success interval, never server
retry deadlines. Unsafe cleanup/history/repair states stop automation and retain
evidence. Failures do not become UpToDate or installation success. Existing
rollback, exact process identity, file hashes, ACLs and readiness receipts remain.

Stopping the monitor never waits on its own install task. Shell shutdown separately
drains the check session and the install session's accepted/safe-to-close boundary,
avoiding a circular shutdown wait. Timer/network callbacks are removed on disposal;
activation-disabled/unsupported installations schedule neither automatic work nor
network subscriptions.

Window checks are best-effort snapshots, not an OS desktop lock: another program
can restore a window after the last check. Actual Windows focus/monitor/restart
behavior still needs live qualification; no claim of a mathematically gap-free
privacy transition is made by fixtures.

## Enforcement limits and remaining gates

This is required policy inside this version of the running App, not OS/device
management. A user/administrator can terminate the process, block networking,
uninstall, modify local software or install an older binary. Older empty-trust or
activation-disabled builds cannot retroactively acquire compulsory behavior.
One authorized qualified complete installer must bootstrap App and launcher.

`UpdateRuntimePolicy.UpdateOperationsEnabled` remains false and the publisher's
minimum Windows build remains null. Public `release-key-1` trust remains pinned.
Environment/secret setup, qualified test bootstrap/activation, real Windows 10/11
A-to-B and separate release approval are still required. This source-delivery PR
does not read the owner's private key, configure GitHub or authorize publication.

## Evidence

Unit fixtures cover required pipeline, reconnect/single-flight, daily persisted
admission, failure cooldown, Retry-After, safety deferrals/rechecks, direct backend
cancel/discard refusal, unusable-stage refresh, locked preferences, stop/drain and
self-shutdown boundaries. Compiled WPF smoke checks verify locked controls, hidden
cancel/discard actions and no activation-disabled monitor. Fixtures use synthetic
data/keys and do not execute a real installed-App update.

Network hints use [.NET NetworkChange](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkchange.networkavailabilitychanged).
See [signing readiness](update-signing-readiness.md), [public trust](update-public-trust.md)
and the [live qualification runbook](update-qualification-runbook.md).
