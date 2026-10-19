# Signing readiness and isolated update qualification

Status: owner-approved Preview trial preparation, updated 2026-10-19 (Africa/Cairo),
following merged PR #60 at `c4c41af`. Owner-authorized environment setup is verified
through GitHub's API, including explicit Boolean `can_admins_bypass: false`.
The public environment variable `PRIVACYMASK_UPDATE_KEY_ID=release-key-1` has been
set and read back. The owner completed direct secret upload and supplied its
success/name/timestamp output; GET-only metadata inspection also confirms the
signing-secret name exists, without reading its value. This delivery prepares
Preview activation, never stable qualification. No private key is read locally,
no secret is uploaded by the agent, and no tag or release is published.

## Actual starting point

- Source is `2.0.0-beta.14` / Windows file version `2.0.0.14`. The owner approved
  a [simple Windows 11 A/B Preview trial](update-preview-trial.md), without requiring
  a VM for ordinary use/basic consenting upgrade testing. Preview operations are
  enabled in the new binary; stable stays disabled. Only beta.14 checks every
  10 minutes after success; beta.15 and later default back to 24 hours.
- App update UI, owned verified staging, privacy-aware automatic restart, authenticated handoff,
  stable launcher and bounded recovery/history are implemented. They are not
  evidence that a real signed A-to-B update has passed on supported Windows.
- `build/update-release-policy.json` pins `release-key-1` and sets build 22000 as a
  Windows 11 technical filter for the Preview, not a qualified minimum OS claim.
  App/launcher compile the same pin and reject stable runtime activation.
  Protected source admission permits prerelease publication only. Exact-tag
  approval, successful production signing and a real device update remain pending.
- Initial GitHub inspection returned zero environments. Following separate owner
  authorization, `privacy-mask-release` now exists with exactly one required
  reviewer (`mahmoudSaaadallah`), self-review allowed and exactly one selected
  deployment rule: tag `v*`, with no branch rule. The production GET-only
  `assert-release-environment.ps1` passed against this configuration on 2026-10-19.
  The strengthened assertion also requires explicit Boolean administrator-bypass
  denial; missing, null, string, numeric or enabled bypass metadata is rejected.
- The owner approved **SoloMaintainer**, public-key integration and now the scoped
  environment setup, owner-operated secret upload and this limited Preview
  activation experiment. Exact publication approval and stable qualification
  remain separate. Passing the API guard proves only the currently
  returned configuration, not per-run approval, key custody or live updates.

## Approved review model: SoloMaintainer

`build/update-signing-approval.json` records the reviewed policy. The authorized
environment setup now matches the following API-verifiable requirements:

| Setting | Required value |
| --- | --- |
| Environment | `privacy-mask-release` |
| Required reviewer | Exactly one User: repository owner `mahmoudSaaadallah` |
| Prevent self-review | Off, deliberately: the owner may approve their own run |
| Deployment restrictions | Selected tags only: exactly one tag rule `v*`; no branch rule |
| Approval | Owner manually reviews each exact source/tag/workflow before signing |
| Administrator bypass | Explicit Boolean `can_admins_bypass: false`; guard rejects unknown/enabled values |

This is human approval by a solo owner, **not independent review or a second
factor**. Protect the GitHub account and recovery methods separately. Future
independent review requires a reviewed policy change and `prevent_self_review`
enabled, not silently adding a second person to this policy.

The no-environment `validate-release-environment` job first validates the merged
tag/source, then performs GET-only environment/reviewer/tag-policy checks. The
signing job depends on its success. Reviewer, self-review, administrator-bypass
and exact tag-policy checks all run before signing admission. Missing,
inaccessible or mismatched settings fail closed before signing-job admission;
the preflight does not create an
environment. This matters because GitHub can otherwise create a referenced missing
environment without protections. See [GitHub environment behavior and approval
settings](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments).

The guard checks configuration at that point, not the eventual approval event,
key custody or qualification results. The live environment response includes
`can_admins_bypass`; only explicit Boolean false passes. If an API response stops
providing this field, admission fails closed instead of assuming bypass is off.
Restrict who can change environment settings and recheck before approval: no
read-only preflight prevents an administrator changing them afterwards. See
[environment metadata](https://docs.github.com/en/rest/deployments/environments),
[tag-policy metadata](https://docs.github.com/en/rest/deployments/branch-policies)
and [job prerequisites](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds).

### Protection recheck and owner secret handoff

Browser automation was unavailable during initial setup. A subsequent API read
returned `can_admins_bypass: false`; no further bypass mutation was needed. The
strengthened GET-only guard now verifies that flag automatically. Before secret
entry or each release approval, the owner should also inspect the UI:

1. Open [repository environments](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/settings/environments)
   and select `privacy-mask-release`.
2. Confirm **Allow administrators to bypass configured protection rules** is
   deselected. If it is not, stop and correct it before secret entry or approval.
3. Keep **Required reviewers** set to the owner only and **Prevent self-review**
   deselected, as explicitly approved for SoloMaintainer. Do not add branch rules
   or loosen the existing tag-only `v*` rule.
4. Rerun the GET-only assertion below, which now checks the API bypass flag as
   well as reviewer/tag policy. Do not use a signing run to test configuration.

This manual step follows [GitHub's environment setup instructions](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments).
No test tag or signing run is needed to verify these settings. Do not add the
private PEM or its passphrase to chat, this document or a PR.
The completed owner-operated step is documented in the [direct secret-input guide](update-signing-secret-setup.md).
The key-ID variable is public metadata, not a substitute for the private secret.

## Full qualification backlog and approval boundaries

The newer [Preview A/B runbook](update-preview-trial.md) is the explicitly approved
smaller experiment; the full disposable-target/fault matrix below is still pending
for stable qualification. Do not relabel those cases Pass after one normal upgrade.

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
   key tests. Stable activation stays false; the owner separately approved Preview
   activation with the Windows 11 technical filter. A downloaded manifest cannot
   teach a client a signing key.
   Authenticode is a separate decision; no production private key is needed for
   this integration or its tests.
3. **Protected GitHub setup — configuration and secret-name metadata verified.** The
   separately authorized environment/reviewer/tag configuration passed its live
   strengthened GET-only guard, including administrator bypass disabled. Only
   public key-ID variable has been added by the agent. The owner, not the agent,
   completed the private input. The private PEM belongs only in environment secret
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
offline fixtures. It passed after the authorized API setup; rerun it to detect
configuration drift, including bypass. It does not verify secret contents or readiness:

```powershell
./scripts/assert-release-environment.ps1 -Repository mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps
```

Next owner actions: review/merge the Preview activation delivery, approve exact
A's tag/source and protected signing job, then follow the basic A/B runbook with
the consenting friend. Confirm custody/account/recovery protections and arrange
disposable Windows targets separately for full stable/fault qualification.
Environment creation, solo review and the public pin do not need to be approved
again; no further private-key generation is needed merely to finish this plan.
Exact release publication and stable activation still need separate authority.
