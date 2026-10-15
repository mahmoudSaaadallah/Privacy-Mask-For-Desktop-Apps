# Stable launcher and managed-install migration

Status: phase 5d.3 is implemented for the Windows x64 testing delivery
`2.0.0-beta.5`. This slice creates the stable launcher entry point and migrates
manual installations to protocol-v1 managed layout. It does not enable the
in-app update UI or make network requests.

## Installed layout

The per-user installer now creates:

```text
%LocalAppData%\PrivacyMask.Desktop\
  PrivacyMask.Launcher.exe
  managed-install.json
  settings.v1.json
  settings.v1.json.bak
  app\PrivacyMask.App.exe
```

The native-AOT launcher remains outside the replaceable `app` directory.
Desktop, Start Menu and launch-at-sign-in entry points target the launcher.
The marker contains a random installation identity and updater protocol; a
normal reinstall preserves that identity. The installer protects the complete
tree with current-user-only ownership and access rules required by the update
recovery boundary.

## Startup and recovery behavior

On every managed start the launcher:

1. accepts only the normal or `--minimized` invocation;
2. validates its exact managed path, marker and private ACLs;
3. acquires the exclusive install lease;
4. completes or rolls back an interrupted update journal before launch;
5. validates the exact single-file application payload and product identity;
6. starts the application while still holding the lease.

A journal that cannot be recovered leaves the installation in repair-required
state. The launcher displays one generic repair message and does not execute an
untrusted or ambiguous payload. It never weakens ACLs or silently adopts an
unknown install marker.

## Installer migration

Running the beta.5 installer once is the migration path from the legacy flat
layout. The transaction stages the App, launcher and marker, preserves settings
and their recovery copies, stops only the exact installed App path, swaps the
directory, rewrites shortcuts and migrates an existing startup command without
enabling startup for a user who disabled it. A failure after replacement
restores the prior directory, shortcuts and startup value.

Only the complete single-file release payload can create a managed install.
Portable folder output remains a developer/runtime artifact and must not be
partially copied into the single-file managed layout. The uninstaller recognizes
both legacy and managed App paths so the migration release remains removable.

## Automated evidence

- launcher boot tests cover clean launch, minimized arguments, interrupted
  rollback, committed-journal archival, invalid payloads and repair-required
  refusal;
- startup-path tests prove that only a valid private managed installation is
  registered through the launcher;
- installer tests cover clean install, exact running-process shutdown, settings
  retention, stable install identity, private ACLs, legacy migration, startup
  migration, rollback and uninstall;
- entry-point tests reject incomplete and portable-only release payloads;
- package smoke checks compare both installed executable hashes and require
  shortcuts to target the packaged launcher.

CI publishes the launcher with Native AOT. A source-machine publish therefore
also needs the Windows C++ linker workload; end users installing the release ZIP
do not need Visual Studio, the .NET SDK or administrator rights.

## Remaining boundary

This slice makes managed startup and recovery reachable, but it does not expose
Check for updates or Update now. Authenticated old-process shutdown, runtime
consent and panic/edit gates, update coordination/UI, production signing setup
and live A-to-B fault qualification remain later deliveries. Until those land,
installing a newer release ZIP is still the supported update operation.
