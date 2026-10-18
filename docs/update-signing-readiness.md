# Signing readiness and isolated update qualification

Status: public-key preparation, updated 2026-10-19 (Africa/Cairo), following merged
PR #56 at `40a8725f052e77ab0ecfe4015f03f21678cd14f1`. The owner supplied and approved
the public identity and reported a successful encrypted-backup restore. This
delivery does not access private keys, create GitHub resources, change repository
settings, enable runtime update operations or publish.

## Actual starting point

- Source is `2.0.0-beta.13` / Windows file version `2.0.0.13`. The
  [required automatic update policy](update-mandatory.md) is implemented but still
  gated by disabled runtime activation, unqualified Windows baseline and signing setup.
- App update UI, owned verified staging, restart consent, authenticated handoff,
  stable launcher and bounded recovery/history are implemented. They are not
  evidence that a real signed A-to-B update has passed on supported Windows.
- `build/update-release-policy.json` pins `release-key-1` and retains an unqualified
  (`null`) minimum Windows build. App/launcher compile the same public pin, with a
  separate disabled runtime activation gate. Production signed publication and
  live in-app update remain blocked. See [public identity and tests](update-public-trust.md).
- Read-only GitHub inspection of this public repository, with an authenticated
  administrator, returned zero environments. No `privacy-mask-release` environment
  exists. No secrets were read and no environment was created during preparation.
- The owner explicitly approved **SoloMaintainer in the plan**, then separately
  authorized public-key integration. Neither approval grants environment, private
  secret transfer, runtime activation or publication authority.

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

1. **Custody — owner-reported restore complete; protection checks pending.** The
   owner privately generated a key, kept an encrypted PKCS#8 copy and reported
   successful decryption of an independently downloaded backup with the approved
   public fingerprint. Confirm account/recovery protection, private sharing and
   separately protected passphrase recovery. The agent has not read the private
   key or audited those storage settings. No private material belongs in chat,
   repo, PR, command argv, logs or qualification reports.
2. **Public roots — authorized integration, not activation.** Pin the reviewed
   NIST P-256 public identity `release-key-1` in publisher policy and shared compiled
   App/launcher policy, with fingerprint/drift and unknown/impersonated/mismatched
   key tests. Keep runtime activation explicitly false and minimum Windows build
   unqualified. A downloaded manifest cannot teach a client a signing key.
   Authenticode is a separate decision; no production private key is needed for
   this integration or its tests.
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
   empty-trust or activation-disabled installations first need an authorized
   qualified bootstrap installer A; they cannot acquire initial trust/activation
   via this disabled updater. Record installed
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

Next owner decisions: confirm custody/account/recovery protections, available
disposable Windows targets and authorization for environment/secret setup and
later limited activation/publication stages. Solo review and the public pin do not
need to be approved again; no further private-key generation is needed merely to
finish this plan. Runtime activation and releases still need separate authority.
