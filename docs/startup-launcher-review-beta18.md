# beta.18 startup-registration and launcher review

Source target: `2.0.0-beta.18`, FileVersion `2.0.0.18`, not published by this
change. The beta.17 diagnostic launcher detection remains **unresolved**. This
delivery corrects independently reproducible startup-registration behavior;
it does not classify the detection as a false positive or certify antivirus
compatibility. The personal installation and quarantined files are untouched.

## Scope and evidence

The review followed `StartupRegistrationService`, its `ShellCoordinator` startup
and settings calls, the installer/uninstaller and shortcut helper, launcher
entry/boot/apply paths, managed startup resolution, update download/staging,
manifest verification and transactional replacement. This is a bounded source
review with regression tests, not a complete security audit or malware analysis.

The original incident is recorded in [the packaging trial](full-app-packaging-trial.md).
Defender reported `Behavior:Win32/Persistence.A!ml` and quarantine, associating
the launcher with shortcuts and HKCU Run registration. Those associations do
not identify the triggering operation. No reproduction on the personal
installation or attempt to circumvent quarantine was made.

## Reproduced defects and corrections

| Trigger | Before | beta.18 |
| --- | --- | --- |
| Start the App with the existing desired startup command | Create/open the Run key for writing and write the same value again | Read first; unchanged command requires no write |
| Startup is disabled and no value exists | Create/open the Run key, then attempt removal | No creation, write or delete |
| Existing value merely contains the executable path inside another command | Substring matching reports startup enabled, potentially overriding loaded preferences | Only the full quoted executable command, with the supported optional `--minimized`, is recognized |
| Reinstall with an unchanged launcher startup command | Rewrite the same command | No migration write |
| Install/shortcut failure before startup migration | Rollback rewrites or deletes startup regardless of whether it changed | Restore startup only after a migration write was attempted |
| Legacy flat/App registration needs migration | Substring-based recognition | Exact known legacy/App command migrates once to the stable launcher, preserving minimized preference |
| Installer/uninstaller encounters wrappers, extra arguments or another executable containing the same path | Could overwrite/delete an unrelated command | Leave the unknown command untouched |

State inspection is read-only. Windows path/switch casing and outer whitespace
are accepted on inspection; reconciliation writes canonical spelling/spacing
once if requested and different. Supported persisted commands remain:

```text
"<the exact executable path>"
"<the exact executable path>" --minimized
```

This is deliberately not a general command-line parser. Other arguments and
shell wrappers are not accepted as a valid PrivacyMask startup registration.
The App's existing settings reconciliation still applies the saved launch
preference; a missing external registry value does not create a new persisted
opt-out policy. First-run launch-at-sign-in defaults and explicit settings
changes remain supported. Read/write errors propagate to existing error handling;
no permissions are changed or retried through elevated access.

The registry adapter opens existing values read-only and reads raw values with
`DoNotExpandEnvironmentNames`. Only a needed enable/change opens/creates the key
for writing and writes `REG_SZ`; disabling an existing value opens an existing
key without creating it. There is no polling/re-registration loop added.

## Launcher and updater boundaries retained

- The normal launcher accepts no arguments or the single `--minimized` argument,
  checks its managed location/ownership/layout, handles bounded recovery and
  starts the fixed App path using `UseShellExecute=false`.
- The apply path parses a specific update request, verifies managed install and
  operation identities, authenticates its App through the existing handoff and
  verifies update inputs before requesting shutdown.
- Manifest verification uses the pinned P-256 public key and authenticated
  metadata. Download/staging verifies signed lengths/hashes, archive layout and
  executable identity. Transactional replacement retains exact baseline,
  readiness and rollback checks. The reviewed paths do not invoke a general
  `cmd.exe`/PowerShell command supplied by a release description.
- No launcher source logic, Native AOT setting, bundle compression, update
  manifest, key, channel, cadence, protocol or settings schema was changed.
  No code obfuscation, antivirus-dependent behavior or rebuild to get past a
  detection was introduced.

The existing update-package signature is application-specific authentication;
it is not a Windows Authenticode publisher signature. The reviewed publishing
script has no Authenticode signing step. Publisher signing is a separate
distribution decision and would not, by itself, establish that this behavior
detection is resolved. No certificates or private signing keys were accessed.

## Isolated validation

- **12** new C# cases use a memory-only registry store: 25 repeated unchanged
  starts, missing disabled registration, single writes/deletion for real preference
  changes, exact command matching, non-string values and access-denied failures.
- **29** PowerShell value-only cases cover matching and migration without registry
  access, installation or process execution; exercised on Windows PowerShell 5.1
  and PowerShell 7.
- The existing transactional installer fixture now maps its process-local HKCU
  PowerShell drive into a unique owned `Software\PrivacyMask.Tests\Installer-*`
  key. This is not Windows' real startup key. The original drive is restored
  and the bounded test key removed in cleanup. The former test briefly modified
  the real Run value and then restored it; it no longer does so.
- Transaction checks record zero writes for a fresh missing entry, stopped-only
  refusal, unchanged reinstall and unrelated shortcut failure. Legacy migration
  writes once. An injected error after the fixture write proves both the prior
  command and installation are restored. Unknown wrapper commands survive
  installation/removal. Only an owned renamed `PING.EXE` is executed for the
  running-process fixture; no production App or quarantined launcher is started.
- Local verification passed **937** unit tests, solution build with zero warnings
  and errors, **54** PowerShell syntax checks, policy checks, installer rollback,
  entrypoint dispatch and compiled GUI/layout/interaction checks. The existing
  **23** offline packaging-trial checks also pass. No full-App measurement or
  eight-hour run is claimed.

Local logs: `artifacts/startup-review-verify-beta18-20261101.log`,
`artifacts/startup-review-installer-20261101.log` and
`artifacts/startup-review-installer-pwsh7-20261101.log`. Logs and binaries remain
ignored local artifacts; personal settings/window contents were not read.

## Remaining work and limits

The original diagnostic packet remains stopped and is not repaired or reused.
A source fix/green CI is not a verdict about Defender. Antivirus classification,
controlled startup detection testing and real-device packaging comparison are
**NotRun/Unresolved** for beta.18. Any later live test needs an agreed isolated
environment with protection enabled; user-device repair or release publication
is a separate action. If Microsoft review is needed, retain the original detection
identity rather than treating a changed file hash as evidence of clearance.

## Primary references

- [Windows Run/RunOnce behavior and guidance](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
- [RegistryKey.GetValue and raw expansion options](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.registrykey.getvalue?view=net-10.0)
- [RegistryKey.SetValue and explicit registry types](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.registrykey.setvalue?view=net-10.0)
- [Microsoft file-analysis submissions](https://learn.microsoft.com/defender-xdr/submission-guide)
