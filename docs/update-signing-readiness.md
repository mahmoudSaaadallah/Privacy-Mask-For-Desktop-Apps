# Signing readiness and isolated update qualification

Status: preparation only, audited 2026-10-19 (Africa/Cairo), after merged PR #55
at `b305507ef20b526c1e758c7e0c132fc25d7868cc`. This is not approval to generate
production keys, create GitHub resources, change repository settings or publish.

## Actual starting point

- Source is `2.0.0-beta.11` / Windows file version `2.0.0.11`. This tooling-only
  delivery changes neither the shipped App nor its version.
- App update UI, owned verified staging, restart consent, authenticated handoff,
  stable launcher and bounded recovery/history are implemented. They are not
  evidence that a real signed A-to-B update has passed on supported Windows.
- `build/update-release-policy.json` still has an empty public trust list and
  an unqualified (`null`) minimum Windows build. Runtime trust is also empty.
  Production signed publication and live in-app update remain blocked.
- Read-only GitHub inspection of this public repository, with an authenticated
  administrator, returned zero environments. No `privacy-mask-release` environment
  exists. No secrets were read and no environment was created during preparation.
- The owner explicitly approved **SoloMaintainer in the plan**. No other setup
  or publishing authority follows from that approval.

## Approved review model: SoloMaintainer

`build/update-signing-approval.json` records the reviewed policy. Later, after
separate setup authorization, the intended environment is:

| Setting | Required value |
| --- | --- |
| Environment | `privacy-mask-release` |
| Required reviewer | Exactly one User: repository owner `mahmoudSaaadallah` |
| Prevent self-review | Off, deliberately: the owner may approve their own run |
| Deployment restrictions | Selected tags only: exactly one tag rule `v*`; no branch rule |
| Approval | Owner manually reviews each exact source/tag/workflow before signing |
| Administrator bypass | Owner disables it where available and verifies in GitHub UI |

This is human approval by a solo owner, **not independent review or a second
factor**. Protect the GitHub account and recovery methods separately. Future
independent review requires a reviewed policy change and `prevent_self_review`
enabled, not silently adding a second person to this policy.

The no-environment `validate-release-environment` job first validates the merged
tag/source, then performs GET-only environment/reviewer/tag-policy checks. The
signing job depends on its success. Missing, inaccessible or mismatched settings
fail closed before signing-job admission; the preflight does not create an
environment. This matters because GitHub can otherwise create a referenced missing
environment without protections. See [GitHub environment behavior and approval
settings](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments).

The guard checks configuration at that point, not the eventual approval event,
key custody, administrator-bypass setting or qualification results. The documented
GET response does not provide a reliable bypass-setting proof; verify it manually.
Restrict who can change environment settings and recheck before approval: no
read-only preflight prevents an administrator changing them afterwards. See
[environment metadata](https://docs.github.com/en/rest/deployments/environments),
[tag-policy metadata](https://docs.github.com/en/rest/deployments/branch-policies)
and [job prerequisites](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds).

## Execution stages and approval boundaries

1. **Custody decision — pending.** Owner chooses an encrypted secret store, a
   separately protected backup location and a documented restore exercise. A solo
   owner may hold both roles, but backup must survive loss of the primary store.
   Decide access/revocation/recovery before generating a key. No private material
   belongs in chat, repo, PR, command argv, logs or qualification reports.
2. **Production key and public roots — separate authorization required.** Generate
   NIST P-256 outside test automation using the approved custody process. Review
   the public SPKI fingerprint and immutable key ID; only public roots enter a PR.
   Update publisher policy and compiled App/launcher trust together, with negative
   tests for unknown/revoked/mismatched keys. A downloaded manifest cannot teach
   an untrusted client a signing key. Authenticode is a separate decision.
3. **Protected GitHub setup — separate authorization required.** Create the exact
   environment above, set its reviewer/tag restrictions and verify bypass in UI.
   Store the private PEM only as environment secret
   `PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM`, with environment variable
   `PRIVACYMASK_UPDATE_KEY_ID`. Use an approved secret-input mechanism, never
   plaintext terminal arguments. Test the read-only gate afterwards; reject extra
   branch/tag policies and a different reviewer. Do not push a tag to test setup.
4. **Isolated lab preparation.** Use disposable Windows 10 x64 and Windows 11 x64
   targets with standard non-admin accounts, synthetic profiles and disposable
   PINs. Record actual OS build, DPI/monitors and snapshots. Pick the oldest
   genuinely qualified supported build; a CI runner or fixture's 19041 is not
   baseline evidence. Windows image/licensing/VM availability still needs owner
   confirmation; the packet generator provisions none of these.
5. **Bootstrap A, then B — separate publication approval required.** Build reviewed
   distinct versions from exact merged commits, with agreed public roots. Existing
   empty-trust installations first need a one-time manual bootstrap installer A;
   they cannot securely acquire the first root via this updater. Record installed
   launcher identity separately: protocol-v1 updates replace only the App, not the
   launcher. Publish the exact approved A/B assets on the selected Preview channel
   through the protected flow. Never reuse a version/tag or alter released assets.
6. **Execute and review evidence.** Follow the [lab runbook](update-qualification-runbook.md).
   The packet contains 25 cases per OS, all initially `NotRun`: normal upgrade,
   cancellation, edits/Panic/PIN, channel/network/retry, invalid assets, rollback,
   lost receipt, physical faults, live keyboard/DPI and resource stability.
   Automated fixtures supplement, but do not replace, real-App results. Physical
   power-loss needs its own authorization and disposable target; never fill or
   interrupt the owner's host drive. A blocked case cannot be relabeled Pass.
7. **Release decision — pending.** Review all required OS/build/artifact results,
   resource budgets, custody/restore, public fingerprints, bootstrap and rollback
   instructions. Owner approves a limited preview first. Stable publication needs
   its own qualification and exact tag approval; this document is not that approval.

## Prepared tools, not completed qualification

From clean committed source:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-update-qualification.ps1 -Name review-01
```

This creates only `artifacts/update-qualification/packet-review-01/evidence.json`
and a copy of the runbook. It records source/version/planned review policy, keeps
all VM/artifact fields empty and all results `NotRun`, and refuses to overwrite a
packet. Generated local artifacts are ignored by Git. It creates no key, VM,
installation, startup entry, environment, tag or release. Do not upload sensitive
evidence; do not use the unsigned JSON as signing or publishing authority.

Safe regression checks require no production secrets:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-release-environment.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-update-qualification.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
pwsh -NoProfile -File scripts/test-update-manifest-schema.ps1
```

The real environment assertion is a separate GET-only diagnostic, not part of
offline fixtures. It is expected to fail until separately authorized setup exists:

```powershell
./scripts/assert-release-environment.ps1 -Repository mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps
```

Next owner decisions: custody store/backup/restore procedure, available disposable
Windows targets and authorization for the corresponding setup stage. Solo review
policy does not need to be approved again; actual keys, environment and releases do.
