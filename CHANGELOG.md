# Changelog

## Unreleased - Performance baseline tooling (no App version change)

- Add optional strict process-bound measurements with separate warm-up, complete-duration/sampling-coverage checks and explicit MiB/whole-machine CPU units
- Run Overview/Applications/Settings with 42 synthetic profiles in isolated hidden WPF processes, using the 30-second warm-up and three 60-second/500 ms passes; preserve exact source/binary identities and refuse overwriting baseline sessions
- Extend isolated package smoke checks with separate EXE/launcher/ZIP/extracted/fresh-install logical-byte measurements; explicitly exclude first-run caches, personal state and update/rollback storage
- Add short owned-process/GUI tool regressions to CI; no changes to production capture, polling, cache limits, protection defaults or release behavior
- Live blur/resource-stability qualification remains pending the owner's later agreed device session; synthetic GUI numbers are not full-App resource budgets or an optimization claim

## 2.0.0-beta.16 - Usability Preview (not published)

- Explain Panic, pending/invalid edits, visible matching windows and uncertain window inspection as separate safe restart blockers; show up to three profile names and a remaining count with a non-mutating Applications shortcut
- Keep paused/focus-aware visible windows blocking and revalidate live safety at handoff; no PIN bypass, automatic minimization or settings save/discard
- Present one primary update stage with real transfer progress, package/application verification, safe waiting, restart and repair states; preserve known server retry deadlines and plain-text notes without claiming installation success
- Distinguish unsupported system/architecture, outside-install execution and unverified installation, with installed-shortcut guidance; retain all managed-path, ownership and trust gates
- Clarify Protection paused and restore explicitly requested keyboard help on Overview/Security without popups on page focus
- Advance file revision to 2.0.0.16 with unchanged settings/history/updater schemas 13/1/1, mandatory daily cadence, trust pins and release repository
- This delivery is a source PR, not a release, live fault qualification or completion of the subsequent performance/stability stages

## 2.0.0-beta.15 - Preview B (published 2026-10-19)

- Prepare the newer B version for the consenting Windows 11 A-to-B automatic-update trial; the existing exact-version rule makes beta.15 use 24 hours after successful checks while installed beta.14 keeps its 10-minute discovery cadence
- Reuse schema-13 settings, schema-1 check history and updater protocol 1; retain failure/server cooldowns, pinned trust, privacy deferrals and the App-only update boundary
- Include the reviewed release-ID publisher fix for B's future protected release, without moving/rebuilding the published beta.14 tag or changing its assets
- Clarify that launching from the extracted folder is portable use without automatic updates; use the installed Desktop/Start Menu shortcut instead
- Advance file version to 2.0.0.15; the initial source delivery did not publish B. A later separately approved protected workflow published B; the owner subsequently reported automatic transition from installed beta.14 to beta.15. This is reported single-device evidence, not receipt-audited stable qualification

## Unreleased - Publisher tooling fix (no App version change)

- Resolve newly created drafts through authenticated paginated release listings, match the CLI-created URL and validate the numeric release ID; do not use GitHub's published-only tag endpoint for drafts
- Publish and confirm the exact validated release ID, retaining tag, signature-asset metadata, digest/size and prerelease/Latest gates
- Refuse existing releases/drafts before creation and stop on ambiguous results; never resume, delete or select an existing draft automatically
- Cover published-only endpoint 404 behavior, pagination/PowerShell array shapes, duplicate drafts and identity failures with offline publisher regressions; existing beta.14 drafts/tag are unchanged by this source fix

## 2.0.0-beta.14 - Preview activation trial (published 2026-10-19)

- Enable the owner's pinned signed-update verifier for Preview binaries, with a managed Windows 11 x64 runtime preflight; stable activation stays disabled
- Check every 10 minutes after success in exactly beta.14, including build metadata; beta.15 and later return to 24 hours automatically without a preference or history-schema change
- Preserve the 15-minute failure cooldown, server retry deadlines, unknown-history rejection, mandatory verified download and privacy-aware automatic restart
- Set Windows build 22000 as the Preview compatibility filter, not a claim of completed OS qualification; block stable release admission during this experiment
- Show the actual build-specific schedule in Settings and privacy help; cover active monitor startup and signed A-to-B discovery with synthetic download/apply actions
- Advance file revision to 2.0.0.14; settings/history/updater schemas remain 13/1/1. The initial source delivery did not publish; later owner-authorized explicit-ID recovery published the original tagged assets without local private-key access or rebuilding the App

## 2.0.0-beta.13 - Source delivery (not published)

- Add required automatic discovery at startup/reconnect and daily after successful checks, with durable 15-minute failure cooldown and server retry deadlines
- Automatically download newer compatible signed releases and install without confirmation when Panic, unsaved/invalid edits and visible protected windows are clear
- Lock automatic-check/channel preferences and remove download/cancel/discard/install choices in required runtime mode; old opt-out settings cannot disable policy
- Revalidate restart safety during draining, authenticated handoff and immediately before shutdown; preserve normal PIN, settings and recovery protections
- Coalesce reconnect notifications, use one adaptive dispatcher timer and stop without awaiting the install task's own shutdown
- Keep compiled activation disabled, production Windows baseline unqualified and live qualification pending; no signing secret/environment/tag/Release is created

## 2.0.0-beta.12 - Source delivery (not published)

- Pin the owner's P-256 public key `release-key-1` in the shared App/launcher trust policy and publisher policy
- Keep update operations disabled by a separate compiled gate; public pinning alone cannot start HTTP, history access, staging or apply
- Verify the approved public fingerprint and publisher/runtime consistency; reject unknown IDs, impersonated IDs and mismatched fixture signing keys
- Advance Windows file revision to 2.0.0.12 without changing settings/history schemas or privacy controls
- Retain the unqualified Windows baseline and separate environment/bootstrap/activation/release approval gates; no production private key is read or shipped

## 2.0.0-beta.11 - Source delivery (not published)

- Connect Update now to the session-owned verified stage and existing edit/Panic/restart-consent/authenticated-handoff gates
- Serialize install clicks and disable discard/retry during apply; ordinary Exit cancels and drains pre-handoff work without granting consent
- Separate accepted handoff from its own shutdown task to prevent circular waits; never claim installation success before readiness
- Share unchanged bounded history with the Native AOT launcher using generated JSON metadata and honor durable GitHub retry gates during apply checks
- Derive worker compatibility from the pinned prepared settings schema instead of assuming schema 12
- Keep compiled production trust empty; no tag, Release, signing credentials or live installed-App update is introduced

## 2.0.0-beta.10 - Source delivery (not published)

- Connect explicit verified downloads to Settings with bounded progress, cancellation and owned-stage discard
- Share single-flight, private durable admission and global GitHub retry deadlines with discovery
- Require a same-saved-channel candidate checked within 24 hours; downloads never advance successful-check timestamps
- Stage under the managed installation lease while masking/settings/PIN behavior stays active and unchanged
- Drain downloads and UI event continuations during shutdown/suspension; ignore queued late progress
- Keep production trust empty and installation/restart UI disconnected; no tag or Release is published

## 2.0.0-beta.9 - Source delivery (not published)

- Add one-shot delayed startup discovery gated by saved opt-in, approved signing trust and managed-install eligibility
- Persist bounded attempt, per-channel success and global retry history without modifying the settings schema
- Prevent restart/channel changes or interrupted outcome writes from bypassing known server retry deadlines
- Cancel/drain background checks on saved opt-out, shutdown and update handoff; never rearm a recurring poller
- Serialize history across sessions and preserve bounded retry history during manual reinstall
- Keep production trust empty, in-app download/apply disabled and all existing privacy/PIN behavior unchanged

## 2.0.0-beta.8 - Source delivery (not published)

- Add Settings > Updates and tray manual checks sharing one cancellable discovery session
- Persist Stable/Preview and future automatic-check preferences with existing Save/Discard, migrating settings to schema 13
- Report unconfigured trust without HTTP, and distinguish failed/incomplete checks from successful up-to-date results
- Show session-only check status, retry guidance and bounded plain-text release notes; keep download/apply and background checks inactive
- Disclose GitHub connection metadata in Updates/About help and retain local-only sensitive settings/captures
- Add preference/session/WPF lifecycle tests and advance Windows file revision to 2.0.0.8

## 2.0.0-beta.2 - Unreleased

- Prevent information popups from opening automatically on focus or page navigation
- Keep Overview/Security visual help hover-only while retaining accessible help text and explicit keyboard help elsewhere
- Use the About shield/PM/PrivacyMask sidebar identity and navy background consistently across all sidebar pages
- Add routed focus/key, inherited hover policy and persistent branding regression checks
- Advance Windows file revision to 2.0.0.2 without changing settings schema or protection behavior

## 2.0.0-beta.1 - Source snapshot (not published)

- Prepare the first identifiable 2.0 testing delivery; stable 2.0.0 still requires final qualification
- Show the semantic prerelease version in About and Settings, with full source metadata in About version help
- Keep numeric assembly/file identities separate; beta.1 uses Windows file version 2.0.0.1 without changing settings schema 12
- Reject stale or mismatched product/file/tag identities during package verification
- Classify beta/rc releases as Pre-release and non-Latest, and require an existing matching tag before publication
- Add local and CI version-policy checks using mocked publishing, without creating tags/releases

- Match the About reference with shield page branding, separate developer/purpose/privacy cards and compact support/profile actions
- Adapt About cards and email actions to wide, compact and stacked layouts while retaining authoritative contacts, assembly version and capture warnings
- Verify unclipped About actions/email, accessible contact routes and non-mutating navigation/feedback without launching external apps
- Align Add/Relink, PIN setup/change/unlock and first-launch windows with the supplied reference; leave About unchanged
- Add explicit per-field PIN visibility controls with plaintext-display and closed-input cleanup
- Open advanced zones in a full-width editor sharing Save/Discard and preserving all ten normalized zone settings
- Qualify synthetic picker search/relink, startup switches, PIN errors/cleanup and advanced navigation at bounded layouts
- Align Shortcuts, Security and Settings with the supplied reference: compact keycap rows, status cards, startup switches and inline branding
- Keep shortcut conflict/pending feedback data-driven and expose non-mutating explanations of focused/global panic
- Retain About navigation and local configuration-path copying in the compact Settings layout
- Align Overview, Applications and profile details with the approved visual reference: flush sidebar, protection banner, grouped application rows, switches, badges and teal blur tracks
- Replace profile activation dropdown with bound radio controls, add accurate color swatches and rounded search/profile fields
- Hide the saved-status footer on clean list/overview pages while retaining it for details and pending edits
- Rebuild complete source checkouts from the distribution installer instead of silently reinstalling a cached executable after pull
- Test source/release dispatch, failure exit codes and prevention of stale-build fallback in local and GitHub verification
- Add isolated compiled WPF layout/edit-session smoke checks to local verification and Windows CI
- Keep compact application details within the viewport while retaining advanced-grid scrolling and grouped reveal dimensions
- Move keyboard focus into newly selected pages and back to application search
- Record repeatable synthetic GUI resource measurements and outstanding live release-qualification gates
- Move About directly into the sidebar with developer information, assembly-based version, privacy/capture limitations, and email/copy/GitHub/LinkedIn actions
- Clarify focused and global panic behavior and saved-PIN requirements on Security
- Apply shared styles and bounded scrolling to PIN dialogs; clear password fields when dialogs close
- Refresh onboarding into concise enrollment, blur/reveal, panic, and startup sections using the branded icon
- Add a protection overview with live profile counts, bounded quick blur controls, and navigation to all configured applications
- Connect Overview global pause and panic actions to existing runtime handlers and PIN verification without persisting runtime state
- Explain shortcut scope, hold delay, registration conflicts, and pending enable changes on the dedicated Shortcuts page
- Add startup preferences and a copyable local settings path with an open-folder action
- Add searchable compact application cards with bounded executable-icon caching and contextual pointer/keyboard help
- Move profile appearance and hover controls into a focused detail page while retaining all advanced zone fields and presets
- Open newly enrolled profiles directly, preserve profile navigation across Save/Discard, and expose missing-target relink recovery
- Add running-application search and an explicit selected executable path to the enrollment and relink picker
- Add six sidebar destinations sharing one settings edit session: Overview, Applications, Shortcuts, Security, Settings, and About
- Keep unsaved edits across navigation and report pending, saving, and failed-save states
- Keep settings open after Save or Discard, restore all saved values on Discard, and update previews for nested settings changes
- Prevent runtime status changes from marking settings dirty, and preserve unrelated edits when shortcut or tray changes are saved
- Introduce the approved private-view application icon across the executable, desktop windows, notification tray, and installed shortcuts
- Add shared GUI colors, spacing, cards, accessible focus outlines, and action-button styles for the upcoming sidebar redesign
- Show live per-profile states for protected, paused, focus-waiting, disabled, stopped, and missing-target applications
- Add a custom-profile relink flow that changes the target executable while preserving mask appearance, strength, zones, hover settings, and profile identity
- Persist executable paths for newly selected applications and migrate the optional field through settings schema version 12
- Let `Ctrl + Shift + P` automatically add and immediately protect the focused desktop application when it has no profile yet
- Restore a disabled matching profile from the same shortcut instead of creating a duplicate, while retaining system and security-process exclusions
- Enable mouse-hover reveal for every newly added application and migrate existing custom profiles to the same behavior
- Add a local 4–8 digit panic unlock PIN that can be created or changed from settings
- Require the PIN before disabling either a focused-application panic mask or the global tray panic mask
- Store only a salted PBKDF2-SHA256 PIN verifier and never persist the PIN itself
- Require holding `Alt` alone for 0.5 seconds before revealing the focused app; pressing any other key cancels that hold until Alt is released
- Use a 600×70 pixel hover reveal window by default and migrate only profiles that still use the previous 394×42 default
- Enable launch at sign-in by default for new installations while preserving the saved choice for existing users
- Migrate the updated profile defaults and panic PIN credential through settings schema version 11
- Move blur adjustment to `Ctrl + Alt + Shift + ↑` and `Ctrl + Alt + Shift + ↓`, migrating the previous Windows-key bindings while preserving custom shortcuts
- Scope `Ctrl + Shift + P`, `Ctrl + Shift + H`, and hold-Alt reveal to the focused protected application instead of changing every protected app
- Keep tray pause and panic mask global so an explicit global control remains available
- Enable mouse-hover reveal for new and existing custom profiles while retaining the full-window privacy mask outside the reveal area
- Resolve shortcuts from focused tool windows and bound unresponsive window capture so unsupported apps retain a safe fallback without repeated capture work
- Migrate custom reveal behavior and shortcut defaults through settings schema version 9
- Add an application picker for visible desktop programs with executable icons, friendly names, process names, and window titles
- Allow browsing directly to an executable so an application can be configured before it is running
- Let custom profiles be renamed or removed while built-in WhatsApp and Telegram profiles remain protected from deletion
- Exclude PrivacyMask, duplicate processes, Windows shell surfaces, and security-sensitive processes from custom targeting
- Begin the user-defined application profile foundation for protecting desktop apps beyond WhatsApp and Telegram
- Give every profile a stable identity so multiple custom applications can coexist and receive independent hotkey updates
- Add safe custom-profile defaults, exact process-name matching, and a generic Windows adapter
- Preserve custom profiles and migrate existing 1.x profiles through settings schema version 8
- Add a lightweight wet-glass mask with transparent condensation droplets over live blur
- Expose mask-style selection in the primary per-application settings
- Preserve the selected mask style when window width activates another adaptive preset
- Add focused-app shortcuts to adjust blur strength by 5%
- Save shortcut adjustments atomically and prevent key-repeat from issuing duplicate steps
- Keep live capture running across blur-strength shortcut changes instead of getting stuck on the opaque fallback
- Replace the repeated procedural frost tiles with a live blur of the protected window
- Make the 0–100% blur-strength scale progressively reveal or suppress underlying detail
- Capture bounded window frames in memory, refresh them asynchronously, and clear them when protection is hidden
- Keep a seamless opaque fallback when a protected window cannot be captured
- Preserve solid-redact and opaque panic-mask options for maximum concealment
- Replace the legacy 60–240% darkness control with a clear 0–100% blur-strength scale
- Migrate existing profile and zone intensities to settings schema version 7
- Present friendly mask-style names and percentage-based zone strength in advanced settings

## 1.1.0 - 2026-10-08

- Start every app launch with protection enabled instead of restoring a paused or panic runtime state
- Change panic mode to an opaque black full-window mask that disables reveal gestures
- Prevent the pause hotkey from bypassing an active panic mask
- Update settings schema to version 6 while preserving compatibility with existing files
- Save settings atomically, serialize concurrent writes, and avoid unchanged startup rewrites
- Keep a last-known-good settings backup and preserve malformed JSON during recovery
- Release timers, hotkeys, overlays, windows, and the tray icon reliably during shutdown
- Report global shortcut conflicts in the tray and beside the affected setting
- Handle startup, settings-save, and launch-at-sign-in failures without unhandled exceptions
- Inspect detailed window metadata only for processes that can match enabled profiles
- Cache process identity and effective mask zones across unchanged refresh cycles
- Skip rebuilding overlay brushes and geometry when the rendered state is unchanged
- Use a slower idle refresh cadence with immediate pause and panic updates
- Reuse hot-path collections and lightweight desktop-window snapshots to reduce allocations
- Remove generated Windows executables and runtime files from source control
- Package installable Windows ZIPs with SHA-256 checksums for tagged GitHub Releases
- Document release installation, portable use, updates, and uninstallation
- Add a one-click batch installer that builds and installs directly from a source checkout
- Create Windows shortcuts correctly when the user profile path contains non-ASCII characters
- Roll back live mask previews when settings are discarded and preserve in-progress edits on reactivation
- Add keyboard shortcuts, access keys, and screen-reader metadata to the primary setup flows
- Fit settings, onboarding, and About windows to the available monitor work area
- Validate editable dimensions and privacy zones with actionable errors instead of silently changing values
- Keep advanced privacy-zone editing behind a simpler expandable section
- Make Windows updates transactional with settings preservation and automatic rollback
- Use one product version across assemblies, About, build metadata, release assets, and Git tags
- Smoke test release checksums, archive contents, executable versions, installation, shortcuts, and removal in CI

## 1.0.0

- Initial Windows desktop release of PrivacyMask
- WPF tray application with overlay masking for WhatsApp Desktop and Telegram Desktop
- Hover reveal support
- Configurable mask darkness and hover window size
- Local JSON settings, onboarding flow, and hotkeys
