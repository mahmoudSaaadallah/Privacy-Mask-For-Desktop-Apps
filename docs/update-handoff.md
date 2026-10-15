# Authenticated update handoff contract

Status: phase 5d.4 internal handoff contract is implemented after the
[stable launcher delivery](update-stable-launcher.md). It does not activate
update discovery, add UI, close the running App or replace installed files by
itself. Source remains `2.0.0-beta.5` because this contract is unreachable from
normal runtime paths at that historical boundary. The subsequent
[runtime handoff delivery](update-runtime-handoff.md) connects the App/launcher
adapters in beta.6 and adds the final `ReadyToExit` acknowledgement; compiled
production trust and end-user UI remain gated.

## Purpose

An update worker must not infer permission to close PrivacyMask from a process
name, a public pipe name or an ordinary Exit command. The App must finish the
future Save/Discard, panic/PIN, consent and settings-write gates first, then arm
one exact prepared operation. The launcher must independently prove that it is
talking to the exact App process that created it.

This slice provides that mutual process-authentication boundary:

- the App owns a random, current-user-only, first-instance named pipe;
- every session is bound to the managed install ID and prepared operation ID;
- both peers validate PID, UTC creation time and canonical executable path;
- the App verifies that the connected client is the exact launcher process it
  started before disclosing a fresh 256-bit challenge;
- the launcher verifies that the pipe server is the retained exact App process;
- challenge, exit request, acceptance, receipt and completion use fixed 70-byte
  frames with no paths, settings, PINs, commands or variable-length input;
- each server and client instance is single-use, and cancellation cannot reset
  or reuse a partially consumed session.

`UpdateApplyRequest` also defines the future launcher's exact command-line
contract. Only canonical lower-case `--update-apply`, a valid handoff pipe,
install/operation IDs, positive canonical process identity values and
`normal`/`minimized` restart mode are accepted. Detecting a case-variant switch
still routes to fail-closed parsing instead of normal launch.

## Exchange

1. App creates the handoff server and launches the exact installed launcher.
2. Both sides retain and validate the peer process identity.
3. App sends a fresh challenge only to that launcher process.
4. Launcher returns an operation-bound exit request.
5. App arms its graceful shutdown and sends acceptance.
6. Launcher acknowledges the acceptance; App confirms completion.
7. Only then may the launcher await the retained App process handle and require
   a clean exit before invoking the existing transaction worker.

The receipt/completion tail keeps both processes alive through final peer
validation. A disconnect, wrong process, changed creation time/path, wrong
session, modified frame, replay, truncation or timeout cannot become exit proof.

## Evidence

Unit tests mutate every frame region, exercise wrong sessions/challenges,
truncation, canceled waits, unexpected pipe peers, invalid apply arguments and
single-use enforcement. The bootstrap smoke matrix additionally publishes and
runs separate Windows processes, proves the worker remains blocked before App
acceptance, completes the full five-message exchange, and runs in both
SingleFile and Native-AOT CI jobs.

## Remaining integration

The subsequent [runtime slice](update-runtime-handoff.md) connects this contract
to the real App and stable launcher: it serializes update consent with
Save/Discard and panic state, freezes settings writers, spawns the launcher,
awaits `ApplicationShutdown`, captures clean App exit and calls
`UpdateTransaction`. Production trust keys, update discovery
preferences/UI and live A-to-B qualification remain separate required gates.
