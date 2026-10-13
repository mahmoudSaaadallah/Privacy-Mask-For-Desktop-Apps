# PrivacyMask 2.0 GUI implementation plan

## Approved design

Implement the reviewed navy, warm-white, and teal desktop interface with six
sidebar destinations: Overview, Applications, Shortcuts, Security, Settings,
and About. Application details open under Applications. Adopt icon concept B:
a white eye crossed by a teal privacy slash on a navy rounded square.

The image mockups describe layout and visual direction. Existing application
behavior and verified contact information determine the final labels and
actions. This plan does not implement the redesign.

## Implementation rules

- Use WPF views and shared resources inside the existing application; no browser
  runtime, web framework, or additional UI framework is needed.
- Share one settings edit session across pages. Navigation must not save,
  discard, duplicate, or reset pending edits.
- Keep runtime protection controls distinct from persisted settings. Panic
  unlock continues to require the PIN.
- Keep live preview, profile identity, custom-app enrollment, relink, startup
  defaults, and the existing shortcuts working throughout the conversion.
- Show essential state directly; put secondary explanation in information
  tooltips accessible by pointer, keyboard, and screen readers.
- Display status with text and color. Avoid duplicate Manage/gear actions.
- Preserve every existing advanced zone field, including zone strength, even
  when a simplified mockup does not show it.
- Reuse the current version source, rather than hard-coding 2.0.0 in the UI.
- Keep captured application frames out of design screenshots and persistent
  assets; use synthetic examples for visual validation.
- Retain the existing settings schema unless an implementation actually needs
  a persisted field. Navigation and filtering do not need schema changes.

## Pull requests and granular commits

Each row is a planned focused commit. Include relevant verification with the
behavior it verifies. Split further for independent fixes discovered during
implementation; do not create empty or artificially fragmented commits merely
to increase the count. Every completed commit must build.

### PR 1: Icon and shared visual resources

Implemented on `codex/v2-brand-and-ui-resources`. The current pages retain
their structure while the new keyed resources are available for later PRs.
Window icons are assigned explicitly because an implicit base `Window` style
does not reliably style the application's derived window types. Text inherits
its containing control's foreground to preserve action-button contrast.

| Commit title | Scope |
| --- | --- |
| `feat: add PrivacyMask private-view icon assets` | Create clean master artwork for approved concept B and a multi-resolution ICO; check readability at 16, 20, 24, 32, 48, 64, 128, and 256 pixels. |
| `feat: embed PrivacyMask icon in Windows executable` | Configure the executable resource and WPF window icon. |
| `feat: use branded icon in notification tray` | Replace the generic shield; keep icon lifetime and disposal correct. |
| `fix: assign PrivacyMask icon to installed shortcuts` | Set and verify the shortcut icon reference for desktop and Start Menu, including updates. |
| `refactor: centralize desktop UI design resources` | Define spacing, typography, colors, buttons, cards, focus outlines, and info tooltips. |

### PR 2: Navigation and settings edit session

Implemented on `codex/v2-sidebar-edit-session`: six navigable sections share
one view model, pending edits are tracked through nested zones and profiles,
and Save/Discard keep the selected page open. Existing application forms and
the full About dialog remain available while subsequent PRs refine page
content to match the final mockups.

| Commit title | Scope |
| --- | --- |
| `refactor: add desktop page navigation state` | Add lightweight page selection and selected-profile navigation without rebuilding the settings model. |
| `feat: add PrivacyMask sidebar navigation` | Implement the six destinations, selected state, title area, and branded identity. |
| `feat: track pending settings changes across pages` | Track nested edits, profiles added/removed, zones, relink, startup, shortcuts, and PIN changes; runtime status refresh must not mark settings dirty. |
| `fix: preserve settings edits during page navigation` | Connect navigation to a single edit session; verify going back retains values and preview state. |
| `feat: add shared save and discard controls` | Show pending-change status, validation, saving, and failure states; define Save as persistence with the page kept open and Discard as rollback. Closing the window retains its tray lifecycle. |

Review the concrete Save/Discard behavior in this PR. Do not silently introduce
auto-save for ordinary settings. Existing shortcut enrollment remains saved
immediately as it is today.

### PR 3: Applications and profile details

Implemented on `codex/v2-application-cards`. Cards and detail navigation share
the edit session; filtering never changes the saved profile collection. The
picker has search, explicit target confirmation, browse, and relink support.
Executable icons use a 64-entry cache and a generic fallback. Contextual help
supports pointer and keyboard access. All ten advanced zone columns remain.
Version stays at the unreleased 2.0.0 and settings schema remains 12.

Verification for this phase (2026-10-13): 186 automated tests passed; solution
build completed with zero warnings/errors; all 15 PowerShell scripts passed
syntax validation; transactional installer and isolated 2.0.0 package smoke
tests passed. An offline WPF harness rendered all six compiled pages, the
application list/details, advanced zones, empty search, and synthetic picker.
It checked selector bindings, all ten zone columns, hidden-candidate deselection,
and selected-profile/search retention through Save and Discard. The harness
does not start PrivacyMask's runtime or capture protected content. Live pointer,
screen-reader, multi-DPI, and performance qualification remain in PR 6; offline
renders are not evidence that those interactive scenarios have passed.

| Commit title | Scope |
| --- | --- |
| `feat: add compact application list view` | Render name, icon, status, enable control, and one Manage action. Use bounded icon caching with fallback icons. |
| `feat: filter configured applications by name` | Add search and no-match state without changing the saved collection. |
| `feat: move application help into accessible tooltips` | Provide contextual help through info controls; retain direct visible warnings and recovery actions. |
| `feat: add focused application settings page` | Move name, enabled state, strength, style, color, activation, and hover dimensions into one profile page. |
| `feat: preserve advanced privacy zone editing` | Place preset selection and all zone fields behind an advanced expander, with current validation. |
| `feat: integrate relink and removal into profile details` | Preserve matching safety and profile settings; confirm removal and retain discard-based recovery. |
| `feat: simplify application picker and relink dialog` | Match visual resources, add search, retain running-app selection, refresh, executable browse, and relink mode. |

### PR 4: Overview, shortcuts, and startup settings

Implemented on `codex/v2-overview-shortcuts-settings`. Overview shares profile
references with Applications, counts every configured profile, and renders at
most six quick-control cards. It reuses existing runtime status updates rather
than discovering windows again. Global actions call the tray's existing
handlers; panic unlock uses saved credentials and repeated UI requests are
blocked while an operation is pending. Runtime actions are explicitly separate
from Save/Discard. Shortcut gestures are not remappable; pending changes are
not presented as registered. Settings exposes startup preferences and the local
configuration path with a read-only open-folder action. Version remains the
unreleased 2.0.0; no settings-schema migration is needed.

Verification (2026-10-13): 194 tests passed, zero build warnings/errors, and 15
PowerShell scripts passed syntax validation. The transactional installer and
isolated release-package smoke tests passed. The offline WPF harness rendered
the six compiled pages and checked shared Save/Discard state, profile detail
navigation, pending-action gating, and prevention of a global pause during
panic using synthetic callbacks. Production PIN entry, Explorer interaction,
live overlay performance, screen-reader output, and multi-DPI qualification
are not established by this harness and remain part of final qualification.

| Commit title | Scope |
| --- | --- |
| `feat: add protection overview dashboard` | Compact application summaries, quick strength adjustment, and Add/Manage navigation. Avoid expensive duplicate discovery. |
| `feat: connect overview to global protection controls` | Show actual protection state and explicit Pause all / Panic mask all controls using current runtime handlers and PIN rules. |
| `feat: add dedicated shortcuts page` | Display registered gestures and enable controls, focused-app scope, Alt delay, and actionable conflict status. Do not imply shortcut remapping is implemented. |
| `feat: add startup and configuration settings page` | Move sign-in and minimized controls, show configuration location with open-location action. About has its own destination. |

### PR 5: Security, About, and supporting dialogs

Implemented on `codex/v2-security-about-onboarding`. About is now inline; its
obsolete standalone dialog is removed. The verified support address, developer
profiles and assembly version have one shared source. Contact failures are
reported and the support address is selectable/copyable. Security explains
focused/global panic and pending PIN edits. PIN dialogs retain existing
verification and cancellation, gain shared styling/work-area bounds and
scrollable fields, and clear password inputs on close. Onboarding explains
enrollment, default reveal, blur, panic and startup preferences in short cards.
Version remains unreleased 2.0.0; settings schema remains 12.

Verification (2026-10-13): 196 tests passed, build has zero warnings/errors,
15 PowerShell scripts validated, and transactional installer/release-package
smoke tests passed. Offline compiled WPF renders covered all pages, PIN setup,
change, wrong-PIN unlock and onboarding. Synthetic checks rejected short PINs,
confirmation mismatch, and wrong current/unlock PINs, verified closed-field
clearing, and preserved startup choices. These checks do not exercise live
modal success/cancel, external contact apps, Explorer, clipboard contention,
screen readers or multi-DPI interactions; final qualification remains PR 6.

| Commit title | Scope |
| --- | --- |
| `feat: add dedicated panic security page` | Show credential status and Add/Change PIN; clearly distinguish focused panic from global panic. |
| `refactor: apply desktop styling to PIN dialogs` | Keep current PIN verification, confirmation, wrong-PIN feedback, cancellation, and first-use setup semantics. |
| `feat: move About into sidebar page` | Retain developer name, software engineer description, purpose, privacy commitment, version, and capture limitation. |
| `feat: add developer contact and support actions` | Use `mahmoud.saadallah73@gmail.com` for bugs/suggestions; include mail and copy actions plus existing GitHub and LinkedIn links with failure feedback. |
| `refactor: refresh first-launch onboarding design` | Explain enrollment, blur, hover/Alt reveal, and startup choices; PIN applies only to panic unlock. |

Verified contact values from the current About implementation:

- Developer: Mahmoud Saadallah, Software Engineer.
- Email: `mahmoud.saadallah73@gmail.com`.
- GitHub: `https://github.com/mahmoudsaaadallah`.
- LinkedIn: `https://linkedin.com/in/mahmoudsaaadallah`.

### PR 6: Usability qualification and handoff

Automated qualification implemented on `codex/v2-gui-qualification`: compiled
WPF smoke checks are now repeatable locally and in CI; compact detail layout
and page-focus issues are addressed. A same-machine synthetic GUI comparison
and single-file size comparison are recorded in [gui-qualification.md](gui-qualification.md).
Live Computer Use initialization failed, so keyboard/screen-reader/per-monitor
DPI and full runtime stability gates remain outstanding. This phase is not
marked fully qualified and no stable release is authorized by these checks.

| Commit title | Scope |
| --- | --- |
| `fix: improve keyboard navigation and accessible help` | Verify logical Tab order, focus restore after dialogs, visible focus, Enter/Escape behavior, and screen-reader labels. |
| `fix: adapt desktop pages to monitor size and DPI` | Verify compact layouts, long names, scrolling, and 100/125/150/200% scale; avoid clipped controls and unnecessary horizontal scrolling. |
| `perf: bound GUI refresh and icon resource usage` | Measure idle/open-settings/many-profile usage; make only optimizations justified by measured changes. |
| `docs: update PrivacyMask GUI usage and release checks` | Update screenshots and user guidance; record build, runtime, package, installer, and usability results. |

The two qualification fix commits and performance commit are conditional on
findings; if a check passes without code changes, record its results in the
handoff documentation instead of creating a no-op commit.

## Verification and acceptance

For each PR, build and run relevant existing checks plus targeted tests for
stateful logic. Test edit-session correctness where data could be lost; do not
write tests that merely repeat cosmetic XAML values.

Acceptance scenarios:

1. Navigate through all six sidebar pages and application details using mouse
   and keyboard.
2. Edit multiple pages, navigate back, save, reload, and verify values. Repeat
   with Discard and confirm live overlays return to saved values.
3. Receive runtime status and shortcut updates while editing without losing
   edits or creating false pending-change indicators.
4. Add by picker, browse, and Ctrl+Shift+P; relink, rename, disable, remove,
   and recover unsaved changes with Discard.
5. Increase/decrease blur, hold Alt for 0.5 seconds, and hover with the default
   600x70 reveal size. Check both built-in and custom profiles.
6. Enter/exit focused and global panic; test wrong PIN, cancel, correct PIN,
   first setup, change PIN, and persistence failure.
7. Check long names, empty search, stopped applications, missing targets,
   shortcut conflicts, and failed external links.
8. Verify executable, desktop shortcut, Start Menu, taskbar, and tray icons at
   relevant scales. Check shortcut icons after an in-place update.
9. Verify packaging, installer update/rollback/uninstall, and startup defaults.
10. Compare idle and active GUI RAM/CPU and package size against measurements
    taken before this redesign. Document any measurable regression and resolve
    material regressions before release.

Visual approval of screenshots confirms appearance, not usability testing.
Record any limitation when the available environment cannot verify a scenario.

## Git workflow

- Use a `codex/` branch per PR and retain the current user Git author identity.
- Keep one coherent change per commit, with a descriptive `feat:`, `fix:`,
  `refactor:`, `perf:`, or `docs:` title.
- Append these exact trailers to every commit:

```text
Committed-By-Agent: codex
Co-authored-by: codex <noreply@openai.com>
Co-authored-by: Codex <codex@openai.com>
```

- Push each completed branch, create its PR through terminal/API, attach it to
  the task, and verify CI before handoff.
- Keep the user in control of merge. After a merge, synchronize the next PR
  with main. If parallel review is needed, explicitly mark dependent PR bases.
- Use a merge method that preserves the granular commits if retaining their
  history and co-author trailers is desired. A squash merge replaces them with
  one commit.
- GitHub account association and achievements depend on GitHub's identity
  rules; preserving trailers does not establish ownership of those addresses
  or guarantee an achievement.

## Completion boundary

Visual fidelity follow-up: `codex/gui-reference-alignment` corrects the three
pages against the user's supplied Overview/Applications/app-settings reference.
See [gui-reference-alignment.md](gui-reference-alignment.md) for the concrete
appearance changes, retained behavior and verification boundaries.

This work delivers the approved GUI and icon through reviewable PRs. Final
2.0.0 release qualification remains necessary afterward; tagging, publishing,
or merging a release is a separate user-authorized action.
