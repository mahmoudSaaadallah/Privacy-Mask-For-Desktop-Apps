# Owner public-key bootstrap — beta.12

Source delivery: `2.0.0-beta.12`, FileVersion `2.0.0.12`, AssemblyVersion
`2.0.0.0`. Settings schema stays 13; history schema and updater protocol stay 1.
No tag or Release is created by this delivery. Public trust is pinned, but runtime
network/download/install operations remain disabled pending separate approval.

## Approved public identity

- Key ID: `release-key-1`
- Algorithm: ECDSA NIST P-256, SHA-256; signatures use the existing P1363 contract.
- Fingerprint: SHA-256 of the exact SPKI DER bytes, lower-case hex:
  `62fb7fb06206ad60c725754f525b4eaa6d2cb6645a12a3b8a008c736f534724d`
- Canonical Base64 SPKI:

  ```text
  MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAET51tmvseX6BRpk7j0M+xCnvYyMogUFwUBHYBGaw6MZeoA26Dm/GgGX/bGHr9PVej4XPOR4QTUnoiKlALmTnZ4A==
  ```

The owner supplied this public identity and authorized its integration. Public
DER import, curve, canonical encoding and fingerprint were checked. The owner
reported successful decryption of an independently downloaded encrypted backup,
with the same public fingerprint. This is owner-reported custody/restore evidence,
not an automated production-signing or live-update qualification result. Account
protection, backup sharing restrictions and passphrase recovery remain owner checks.
No private PEM, passphrase, backup link, secret value or personal storage path is
needed or recorded in the repository. Never use the production private key in tests.

## Three separate gates

1. **Public identity:** `UpdateRuntimePolicy.TrustedKeys` compiles the public pin
   into the Updates library used by both App and Native AOT launcher. A fresh
   snapshot prevents callers from replacing the compiled registry. The publisher
   policy carries the same key; an embedded test resource detects drift.
2. **Runtime activation:** `UpdateRuntimePolicy.UpdateOperationsEnabled` is
   deliberately false. Its verifier retains and validates the public pin, but
   `IsConfigured` is false until both trust and explicit activation are present.
   Existing discovery/staging/handoff/apply checks therefore still fail closed.
   Settings, environment variables, CLI arguments and remote metadata cannot
   enable this compiled gate. Generic library fixtures use their own ephemeral
   keys and explicit verifiers; they do not activate the shipped runtime.
3. **Publication eligibility:** `build/update-release-policy.json` keeps
   `minimumWindowsBuild: null`. A public pin is not proof of OS support. Signed
   packaging still rejects the production policy, independently of missing
   environment/secrets. Do not insert a fixture's illustrative 19041 to bypass it.

Consequently Settings/tray still report NotConfigured before network, managed
installation inspection, history creation or startup scheduling. No download or
apply is enabled. Ordinary manual installation/repair still works as documented.

## Why the shared launcher matters

Protocol-v1 updates replace only the App. Older empty-trust installations cannot
acquire this first key from a downloaded manifest or an App-only update. A later
qualified, explicitly authorized complete installer must bootstrap both App and
launcher; simply pulling source does not change an installed executable.

Future normal key rotation must bridge every verifier that will consume the new
key. With the current protocol, a launcher lacking the next root needs a complete
installer update; an App-only bridge is insufficient. Compromise/loss recovery is
separate and must not trust an unverified key announcement or assume offline
clients learned a revocation. See [manifest boundaries](update-manifest.md).

## Regression evidence and limits

The focused tests check the exact public identity/fingerprint, public-only export,
runtime/publisher alignment, immutable registry snapshots, unknown/impersonated key
IDs and a fixture private key falsely claiming the owner ID. They also prove that
the compiled disabled verifier blocks HTTP, history-root inspection, staging-root
inspection and apply; even a valid ephemeral signature cannot bypass its gate.
Existing positive signature/package tests continue with ephemeral keys only.

No positive production signature is fabricated: the agent does not possess the
owner's private key. No production signature, live GitHub environment, real-App
A-to-B, Windows 10/11 qualification or physical-fault result is claimed here.

## Remaining sequence

1. Obtain separate authorization to configure the planned SoloMaintainer GitHub
   environment, reviewer/tag restrictions and signing secret/key-ID variable.
   Transfer the decrypted PKCS#8 PEM directly to approved secret input; never
   commit it, print it or store an unencrypted disk copy. Do not upload the
   encrypted backup file as though it were the signing secret.
2. Prepare disposable lab targets and explicitly authorize the required signed
   bootstrap/test deliveries and limited runtime activation in a reviewed PR.
   Qualify the actual minimum Windows build and source/artifact pair; no silent
   activation follows from this public-key PR.
3. Run the [real-App qualification plan](update-qualification-runbook.md), then
   review evidence before general end-user activation or release publication.

The [signing readiness plan](update-signing-readiness.md) and
[operations guide](update-signing-operations.md) remain the authorization/runbook
references. Neither this document nor a successful fixture grants release authority.
