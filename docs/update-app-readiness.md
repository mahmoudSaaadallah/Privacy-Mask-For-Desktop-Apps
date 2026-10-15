# Authenticated App readiness (phase 5d.2)

Status: **shipped, fail-closed startup foundation; not an activated updater**.
This slice advances source to `2.0.0-beta.4`, Windows FileVersion `2.0.0.4`;
AssemblyVersion remains `2.0.0.0` and settings schema remains 12. It creates no
tag/release, production key, network preference, update button or automatic
installation. Current installs do not contain the required stable launcher, so
the authenticated path cannot yet be entered by the end user.

## Exact startup authority

The App recognizes an update attempt before normal singleton activation only
when the first argument is `--update-start` (case variants are treated as a
malformed attempt, not normal launch). The seven-field worker contract accepts
only canonical pipe, install/operation GUID, parent PID/creation ticks and
`normal`/`minimized` values. Extra, missing, signed, padded or noncanonical values
fail closed without displaying internal paths or exception details.

Production validation requires the running image to be exactly
`%LocalAppData%\PrivacyMask.Desktop\app\PrivacyMask.App.exe`, the parent to be
the future `%LocalAppData%\PrivacyMask.Desktop\PrivacyMask.Launcher.exe`, and the
managed root, app, launcher, lock, marker, journal and operation evidence to be
private current-user paths without reparse points. The install/operation IDs,
AwaitingReadiness journal state, installed target hash and retained original
app/settings snapshots must all agree. The App never takes the worker's exclusive
`.update.lock`; it performs authenticated read-only validation while the parent
retains that lease.

The named pipe is local/current-user/single-use. The App retains and verifies the
parent process handle, PID, creation time, image path and actual pipe-server PID
before accepting the fresh challenge. Frames remain fixed at 70 bytes and carry
only protocol identity/challenge data—never settings, executable arguments, PINs
or arbitrary paths.

## Strict settings migration

Update startup uses the operation's exact prepared primary/backup snapshots. It
rechecks live and retained copies before parsing and immediately before atomic
promotion. A saved primary is authoritative; an absent primary may be restored
from the exact backup; defaults are created only when both were absent at
preparation. Malformed JSON, missing/unsupported schema, invalid PIN credential,
invalid normalized profiles or a changed file fails readiness. There is no normal
corrupt-file fallback that could silently discard a PIN, custom profile or choice.

Migration preserves onboarding, launch/minimized choices, valid PIN verifier,
profiles/executable paths, hover dimensions, hotkeys and mask settings through
the existing schema migration. The normalized current-schema file is written to
a same-directory write-through temporary file and atomically promoted; the prior
primary becomes the backup. The post-migration primary/backup sizes and hashes
are pinned and rechecked immediately before Ready. Rollback remains the launcher's
responsibility and uses the untouched operation snapshots.

## Real App readiness ordering

The update child must become the normal primary singleton. It does not signal a
different instance if that acquisition fails. Interactive onboarding/settings UI
is deferred while it:

1. uses the already validated migrated settings;
2. reconciles startup registration to the stable launcher path;
3. creates the tray shell and attempts every configured global hotkey;
4. builds the protection filter and completes the first overlay refresh; and
5. starts the normal refresh timer.

A reserved hotkey remains a visible nonfatal warning; an actual first-refresh or
shell initialization failure prevents Ready. The App then rechecks the target,
journal and migrated settings, authenticates the pipe and sends Ready. After the
worker durably advances the same immutable journal by exactly one revision to
Committed and sends Accepted, the App rechecks the journal/target and sends the
receipt. Only then can onboarding/settings appear. Minimized/normal preference
comes from the authenticated request, not unrelated command-line text.

Shutdown during this sequence closes the readiness client only after the shell's
accepted operations drain and resources dispose. A precommit failure exits with
an error and lets the worker kill the exact child and restore the retained
baseline. A receipt lost after durable commit must not trigger rollback.

## Evidence and remaining gates

Unit and real named-pipe cases cover canonical parsing, managed layout and ACL
evidence, app/settings mutation rejection, exact parent/pipe identity, Ready/
Accepted/Receipt ordering, missing durable commit, strict primary/backup/default
migration, PIN/profile preservation and shell/commit/UI milestone ordering. The
normal App build and existing transaction/bootstrap suites remain required.

This is not an end-to-end installed update. Next: a small stable launcher that
owns boot recovery and the transaction worker, authenticated graceful old-App
shutdown/consent with edit and panic gates, installer/shortcut/startup migration,
old-version restart and bounded terminal retention. Only after those gates should
discovery/download controls be connected to this path and qualified on Windows
10/11 standard-user installations with approved production signing keys.

See [delivery plan](in-app-updates.md), [transaction worker](update-transaction.md),
[recovery contract](update-recovery.md) and [release process](release-process.md).
