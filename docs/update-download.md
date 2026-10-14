# Verified update download and staging (phase 4)

Status: library and offline tests implemented; not connected to the WPF App.
No update button, scheduler, application replacement, launcher, production key
or release is introduced. App payload/version/settings remain
`2.0.0-beta.2` / schema 12. The first shipped runtime integration must follow the
beta/FileVersion policy. See [delivery phases](in-app-updates.md).

## Host/API contract

`UpdateStager.PrepareAsync(candidate, context, stagingRoot, progress, token)`
returns a `StagedUpdate` only after all checks complete. It never stops/executes
processes, reads PIN/settings, changes masks, shortcuts or sign-in registration.
Downloading can therefore occur while protection remains active; panic/edits
and explicit restart consent remain mandatory **apply** gates in phases 5/6.

The host provides a previously created, dedicated absolute `update-staging`
directory on a fixed local Windows drive. The stager rejects relative/UNC/device
or ADS paths, broad roots and any existing reparse-point ancestor. It creates a
fresh random operation child with protected, non-inherited ACL granting only
the current user's SID full control. Names inside that child are code constants,
never manifest paths. Use `CreateNew`; never overwrite existing destinations.

Managed remains a host-supplied fact, not permission to update an installation.
The stage root is not an install root. Phase 5 must bind it to a canonical owned
managed installation, inspect parent ownership/ACL and launcher/marker identity,
serialize installer/uninstall/apply, and enforce same-volume replacement.
This delivery does not claim to resist an already hostile same-user process,
administrator/OS, or concurrent malicious path/ACL replacement.

Treat discovery snapshots and returned staging paths as data, not authorization.
Before writing, snapshot and reauthenticate the **exact signed bytes**, bind the
candidate's locally constructed release/asset-ID endpoints and displayed manifest,
and recheck newer-version/channel/OS/protocol/settings compatibility. No remote
trust roots, custom production HTTP handlers, private keys or arbitrary URLs.
An asset ID is a locator; signed archive/file hashes are the content authority.

The resulting child contains exactly:

- `package.zip`: complete signed-hash-verified archive;
- `PrivacyMask.App.exe`: verified extracted payload, never executed;
- `update-manifest.json` and `update-manifest.sig.json`: original signed bytes.

`StagedUpdate.Version` is informational. No durable ready/apply journal is
created here. A future worker must independently reauthenticate metadata and
rehash archive/payload, inspect PE/eligibility/paths and require fresh discovery
before applying. Enforce the specified 24-hour successful-check freshness limit
there; this stager does not invent a timestamp or grant cached/offline apply.
Release withdrawal/key revocation also require the later recheck/incident policy.

## Streaming and failure bounds

- Reuse the discovery transport's anonymous fixed-repository GETs, redirect
  allowlist, three-hop maximum and server backoff. No cookies/PAT/Range/resume.
  Signed CDN query tokens remain transient and are never stored in the stage.
- Headers-only completion, then explicit cancellation/deadline for **body reads**,
  hashing and writes. Ten-minute overall default, ten-second connect timeout,
  one operation per stager, no automatic retry. Caller must cancel and await
  pending work before disposing the client.
- Download directly to `package.partial`, hashing with a 64 KiB buffer. Enforce
  signed size on actual reads even without Content-Length; reject a supplied
  mismatched length, truncation, overflow, altered hash and HTTP 206. Only rename
  the complete archive after authentication; an interrupted package is not resumable.
- Inspect ZIP end/central-directory bounds **before** ZipArchive allocation:
  one entry, single disk, central directory <=4096 bytes, no comments/ZIP64.
  The publisher emits this supported v1 subset. Then require exactly one regular
  root `PrivacyMask.App.exe`; reject duplicates, extra files, traversal, ADS,
  symlinks, reparse/directory attributes, and wrong expanded size/hash.
- Archive and expanded file each <=512 MiB, expansion <=100:1 as signed manifest
  rules already require. Bounded async extraction to `payload.partial`; inspect
  AMD64/PE32+, executable-not-DLL, product name, numeric version and exact
  product-version/source SHA before giving the payload its final name.
- Flush new files to disk, but do not interpret flushes as a power-loss recovery
  journal. Progress phases: Downloading, Verifying, Extracting, Ready. Download
  notifications are throttled to 100 ms plus final byte count. Ready means
  prepared, never installed. `IProgress` consumers must remain bounded/nonblocking;
  callbacks can be dispatched asynchronously by the host's synchronization context.
- Preflight staging space = signed ZIP + expanded EXE + 64 MiB margin. This is
  **not** the full apply-space check: old app/settings backups/journal need their
  own later budget and recheck. IO/disk-full failure still aborts after preflight.
- Cancellation/network/validation/IO/progress failure cleans only successfully
  created files and the exact operation directory, non-recursively. Recheck
  reparse points before use/cleanup. Foreign files, locks, denied access or new
  junctions preserve evidence instead of following links or deleting a target.
- `UpdateDownloadException` contains a bounded reason, `CleanupIncomplete`, and
  optional server retry time; no raw network/IO inner exception, user path or URL
  is exposed. Caller cancellation propagates `OperationCanceledException` with
  `Data["UpdateCleanupIncomplete"]`. Do not report cleanup success when false.
- `StagedUpdate.TryDiscard()` explicitly and idempotently removes that handle's
  owned files; false means cleanup could not finish. It cannot accept a caller-
  invented target. A successful stage otherwise remains until the host discards
  or hands it off. Crash-orphan retention/pruning is deliberately pending phase 5;
  never scan/delete arbitrary directories to make this look automatic.

## Evidence and remaining qualification

Unit fixtures use ephemeral signing keys, fake HTTP, owned Temp directories and
no installed user files. Cover reauthentication, changed display/URLs, unsupported
contexts, sizes/hashes with/without length, interrupted IO, redirects/HTTP errors,
cancel/timeout/single-flight/backoff, unsafe signed ZIPs, disk preflight, private
ACLs, unknown-file retention and root/cleanup junction rejection. Existing archive
fixtures additionally exercise central-directory allocation bounds.

`scripts/test-signed-update-package.ps1` now invokes the separate Updates smoke
host against a newly published real EXE and memory-key-signed test assets. It
streams the real ZIP through the same stager with both declared and unknown
length, checks payload identity/hash/metadata/progress and idempotent cleanup.
The handler serves local file streams; no network, EXE execution or installation.
CI already runs that script after publishing the current committed source SHA.

Logical stream-write/read faults are injected; a physically full disk, antivirus
locks, live GitHub/CDN download, UI responsiveness and measured RAM on a slow
machine are **not** proven by these fixtures. Crash journal/rollback, real
release A-to-B, Windows 10/11 standard-user qualification, production key custody
and runtime privacy disclosure remain pending. See [recovery](update-recovery.md)
and [signing operations](update-signing-operations.md).

## Sources

- [ResponseHeadersRead and body timeout boundaries](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)
- [ZIP external attribute mapping](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchiveentry.externalattributes?view=net-10.0)
