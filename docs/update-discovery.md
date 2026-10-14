# Read-only update discovery (phase 3)

Status: library and offline HTTP-fixture tests implemented. The App does not
reference/instantiate this library yet. No button, scheduler, live update check,
ZIP downloader, installer, production key or release is added. Product/settings
versions remain `2.0.0-beta.2` / 12. See [delivery phases](in-app-updates.md).

## Shared trust and host contract

`src/PrivacyMask.Updates` is a framework-only .NET 10 library. Public contracts,
strict JSON and `UpdateManifestVerifier` moved there so release tooling and the
future app use the same rules. Private signing remains in ReleaseTool; Updates
has no private-key PEM importer, signer, environment secret reader, profile/PIN
reference, file writer or process launcher. App/Core/Windows have no project
reference to it, so the installed app's payload, behavior and privacy are unchanged.

The verifier snapshots at most 16 reviewed public P-256 SPKI keys and rejects
duplicate/noncanonical keys. Empty trust returns NotConfigured with zero requests.
Production policy is still unconfigured; the future app must embed reviewed
trust roots, never learn keys from a remote manifest or preference file.
Authentic future-protocol manifests are classified as incompatible rather than
invalid signatures. The publisher separately requires claims to match its policy.

The host supplies installed informational version, channel, runtime, Windows
build, updater protocol, saved settings schema and installation kind. Managed
is a **host fact**, not filesystem authorization. Phase 5 must actually inspect
the marker, canonical paths, ownership/ACL, reparse points and stable launcher.
This library does not inspect disk or grant apply permission. Downloader/worker
must independently revalidate metadata and install eligibility before mutation.

| Result | Meaning |
| --- | --- |
| Available | Highest examined authenticated compatible newer version, with pinned release/package IDs and signed bytes; no ZIP fetched |
| UpToDate | Complete successful bounded listing had no eligible newer published version; never inferred from HTTP failure |
| NoCompatibleUpdate | Newer authentic versions do not meet current OS/protocol/settings compatibility |
| UnableToCheck | Network/timeout/invalid data or all relevant newer versions were rejected |
| Incomplete | Pagination/candidate budget or inconsistent listing prevents a complete conclusion |
| RateLimited | Server retry deadline active; manual clicks cannot bypass it |
| NotConfigured | No pinned trust keys; no network |
| ManualUpdateRequired | Source/portable/unknown/legacy installation or unsupported runtime; no network |
| InProgress | Another check owns this client's single-flight gate |
| InvalidContext | Caller supplied invalid identity/facts; no network |

Caller cancellation propagates OperationCanceledException and releases the gate.
Internal timeout instead returns UnableToCheck. The host must cancel and await
pending work before disposing. The later integration supplies 24-hour background
cadence, opt-out persistence and UI; this client does not schedule itself.

## Selection and transport bounds

- Strict SemVer with overflow-safe numeric comparison. Ignore installed build
  metadata for precedence, reject equal/older replacements, use channel policy
  instead of dates/lexical order, and reject inconsistent prerelease flags.
- Complete listing first, then descending version order. Require unambiguous
  uploaded metadata/ZIP, valid pinned signatures and matching tag/channel/size/
  archive name and supplied GitHub digests. Digests are corroboration, not trust.
- A rejected newest version may fall back only to a still-newer authentic
  compatible candidate with rejected count; never downgrade or silently label
  a failed verification as UpToDate.
- Anonymous GETs only, fixed repository, static User-Agent and supported explicit
  API version. No PAT, cookies, profile/window information, paths or PIN data.
  Handler injection is internal to the test assembly only; production callers
  always use the handler with automatic redirects/cookies/credentials disabled.
- At most five pages of 30 releases, 1 MiB decompressed JSON per page, 16 JSON
  levels, 4096-byte Link header and ten metadata candidates per check. Actual
  stream bounds apply without Content-Length: manifest <=64 KiB, signature <=4 KiB.
- One 45-second overall linked deadline, 10-second default connection timeout,
  one in-flight check and no automatic retry loop/service.
- In-memory cache only: at most five bounded page bodies and ETags, requiring
  server revalidation. A 304 without cache fails. A changed first-page ETag
  invalidates later cached pages. Re-fetch/authenticate signatures on each check.
- Next-link relations, query ordering and numeric GitHub repository aliases are
  hints only; always construct our own fixed named-repo page URI. Reject external
  next hints and duplicate IDs/tags. Budgets return Incomplete, never a guess.
- Metadata redirects only, maximum three hops. HTTPS/default port/no userinfo/
  fragment, exact originating asset/browser route or exact GitHub asset-CDN host
  and release-asset path. Reject lookalike domains, unrelated routes and loops.
  CDN signed query strings are transient, never returned/logged/persisted.
- 403/429/Retry-After use the later of headers/reset and a one-minute fallback,
  capped at 24 hours. After expiry a new check may run. HTTP/timeout failures
  stay distinct from UpToDate; background cadence belongs to integration.
- Release notes are capped at 4096 UTF-16 code units and must remain display-only
  text in the future UI, never trusted HTML/commands. Candidate URLs are locally
  constructed release pages and package asset-ID endpoints, not redirect targets.

Available authenticates discovery metadata only. It does **not** prove ZIP/PE
integrity, filesystem eligibility, startup readiness or installation. Those
remain phase 4/5 gates. No app networking is activated by this delivery.

## Verification and pending gates

`scripts/verify.ps1` runs unit fixtures (filter UpdateVersionTests,
UpdateDiscoveryTests, UpdateTransportTests). Ephemeral keys and fake message
handlers mean no requests leave the machine. Cases cover versions/channels,
keys/binding, context, compatibility, cache, body limits, redirects/pagination,
backoff, cancellation/single-flight and exhausted budgets. Existing publisher
signature/archive tests reuse the shared verifier.

After publishing the current committed EXE, run
`pwsh -File scripts/test-signed-update-package.ps1` to regress real packaging.
It uses test keys and never publishes a release or updates a user installation.
Production trust/environment setup, live discovery, privacy/opt-out UI, real
launcher inspection/migration, upgrade A-to-B and stable qualification remain
pending. See [signing operations](update-signing-operations.md).

## Sources

- [GitHub releases API](https://docs.github.com/en/rest/releases/releases)
- [GitHub conditional request and retry guidance](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api)
- [Semantic Versioning](https://semver.org/)
