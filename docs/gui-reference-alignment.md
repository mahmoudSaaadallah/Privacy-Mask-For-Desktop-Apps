# GUI reference alignment

This follow-up matches the user's three-page reference: Overview, Applications,
and app settings. The earlier implementation retained the navigation model but
did not reproduce the reference's visual hierarchy and controls.

## Visual changes

- Flush navy sidebar with the reference PM wordmark and navigation symbols;
  About remains available and the approved private-eye executable/tray icon stays.
- Narrower default window, warm-white content, teal actions and coral panic.
- Overview protection banner replaces the extra metrics cards; compact app
  cards expose icons, info help, status badges, blur tracks and Manage.
- Applications uses a search field beside Add, one grouped list with row dividers,
  switches, status badges, and direct relink recovery.
- App details uses the name/icon/enabled header, custom teal blur track, white
  selectors, correct color swatches, activation radios and grouped hover fields.
- Save/Discard remains visible in details or whenever edits are pending.
  Clean Overview and Applications do not carry the extra saved-status footer.
- WhatsApp, Telegram and Notepad use lightweight vector symbols based on the
  reference; other apps retain their cached executable icon or generic fallback.

This is a native WPF implementation, not a screenshot overlay. No additional
UI framework, image-generation runtime or captured-content asset is introduced.
PM is the sidebar wordmark; the previously approved B icon is not replaced.

## Behavior retained

Profile identity, stored names, enrollment, relink, removal confirmation, all
ten advanced zone fields, live previews, Save/Discard and shortcut behavior
remain. Short built-in card names do not rename persisted profiles. Radio
unchecking cannot overwrite activation mode. Global pause/panic still call
existing handlers and PIN guards; data shown in badges reflects runtime state.

The overview still bounds its app cards to six and provides the full list when
more profiles exist. Real percentages, app names and runtime states naturally
vary from the illustrative reference. No state or blur percentage is hard-coded
to look protected.

## Verification

201 unit tests passed; builds have zero warnings/errors. Compiled WPF checks
passed at simulated 100/125/150/200% DIP layouts, including activation-radio
round trips, selector sources, ten zone fields and selected-profile Save/Discard.
Transactional installation, six installer-entrypoint cases, 17 script syntax
checks and isolated single-file package smoke checks also passed.

Three separate synthetic preview processes produced Overview, Applications
and Notepad-settings renders at 1000x880 DIP for visual comparison. Previews
contain no real protected content. Native title bars are outside these content
renders; the application retains standard Windows title-bar behavior.
Live keyboard, screen-reader and real per-monitor DPI qualification remain
outstanding as recorded in [gui-qualification.md](gui-qualification.md).
Version remains unreleased 2.0.0 and settings schema remains 12.

## Secondary-page reference follow-up

Shortcuts, Security and Settings now follow the user's second reference.
These pages use its compact inline sidebar branding while the earlier
Overview/Applications/detail layouts keep their original reference wordmark.
About remains a sidebar destination.

- Shortcuts: one grouped panel, concise titles, adjacent information controls,
  configured keycaps, enable switches, an inline 0.5-second hold delay, and
  amber conflict rows. Descriptions move to accessible help. Conflict and
  pending messages follow actual registration/edit state, not the mockup.
- Security: compact PIN status/Add-or-Change action, explanatory Active app /
  All apps tabs, and local-settings information. The tabs explain existing
  controls; they do not activate protection or persist a new panic scope.
  This distinction is visible and included in accessibility/help text.
- Settings: startup switches, assembly-derived About identity/navigation, and
  a configuration-file action. Full path is in information help; right-click
  or Shift+F10 on Show location retains Copy file path. Pending edits retain
  the shared Save/Discard bar.

206 unit tests pass. Compiled WPF checks verify startup-switch binding and
non-mutating panic-tab selection/retention alongside existing 100–200% layout
and edit-session checks. Builds have zero warnings/errors; 17 script syntax
checks, installer transaction/entrypoint checks and the isolated package smoke
test pass. Separate synthetic render processes produced all three page
previews; software rendering is used only by the preview tool, not by the app.
Live external-link/clipboard, keyboard, screen-reader and per-monitor testing
remain outstanding. Version 2.0.0 and schema 12 are unchanged.

## Dialog and advanced-editor reference follow-up

Reference screens 07–11 are implemented; screen 12 (About) is intentionally
excluded. A parsed-markup comparison against merged main confirms the existing
About page is unchanged. The approved executable/tray icon is also retained.

- Add/Relink: compact searchable running-app rows, refresh, separate executable
  browsing, profile name, selected target path, and relink-preservation help.
  Existing process exclusions and enrollment/relink behavior remain unchanged.
- Create/Change PIN: uncluttered fields, 4–8 digit hint and information help.
  Current PIN is shown only when a credential exists. An explicit eye button
  reveals each field independently; hiding clears the plaintext TextBox and
  closing clears both visible and masked inputs. No PIN binding or logging is
  introduced. Clearing controls is not a guarantee of managed-memory zeroing.
- Unlock: target-specific heading, adjacent assertive error, Keep locked and
  Unlock actions. Cancel/incorrect PIN do not authorize unlocking. Copy states
  the actual behavior: removing panic restores ordinary privacy settings, not
  a new timed-reveal mechanism.
- Advanced zones: full-width subpage with hidden sidebar, application identity,
  Back, preset controls and a white grid. It stays inside the existing main
  settings window, not a second conflicting edit session. Save/Discard preserves
  the selected editor and profile. All ten fields remain available, including
  Blur %, which is absent from the illustrative reference. Coordinates are
  explicitly 0–1 fractions, not fabricated 1920×1080 pixel dimensions. The grid
  can scroll horizontally at narrow widths without expanding the entire form.
- First launch: three code-native vector illustrations, compact shortcut help,
  supplied startup preferences as switches, and Get started. Extended privacy,
  capture and reveal guidance remains available in accessible information help.

210 unit tests pass. Compiled WPF checks cover the 100/125/150/200% DIP main-page
matrix, full-width zones, selected-zone Save/Discard, synthetic picker search,
empty state, refresh and relink, startup switch round trips, PIN mismatch,
non-digit/wrong-current/wrong-unlock validation, reveal/edit/hide/close cleanup,
adjacent unlock feedback, and action bounds at nominal and 420×340 layouts.
The internal picker test constructor injects synthetic inventory; these tests
do not enumerate real applications or initialize production privacy runtime.
Builds have zero warnings/errors. All 17 PowerShell syntax checks, installer
transactions, six entrypoint cases and isolated single-file ZIP/hash/shortcut/
install/uninstall checks pass. The installed user app/settings are untouched.

Visual previews are native WPF content renders with synthetic apps and blank PIN
fields, not screen captures of protected content. Preview-only software rendering
avoids offscreen retained-render artifacts; production rendering is unchanged.
Native title bars, successful modal user workflows, screen-reader behavior,
real per-monitor DPI and runtime stability still require the live qualification
gates in [gui-qualification.md](gui-qualification.md). Version remains unreleased
2.0.0 and settings schema remains 12; no tag or release has been published.

## About reference follow-up

The later user request supplies a distinct About design; this follow-up now
implements that page separately from the earlier excluded screen 12.

- About heading and shield/product/version hero, then separate developer,
  purpose, privacy-commitment, support and thank-you/capture-warning cards.
- Lightweight frozen vector drawings supply the shield, portraits and support/
  privacy symbols. The About sidebar uses shield/PM branding and a navy gradient.
  Other page bodies and their branding remain unchanged; a parsed XAML comparison
  confirms all seven other page bodies match merged main. The approved B
  executable/tray/installer icon is not replaced by the decorative About shield.
- Native adaptive panels keep two columns at readable widths and stack otherwise;
  email/copy and Send email similarly share a row or stack. Both the 1000-DIP
  default preview and the 1280-DIP wide preview follow the reference hierarchy.
  The 860-DIP preview stacks with vertical scrolling, not horizontal page scrolling.
- Read-only support email, copy feedback, Send email, GitHub/LinkedIn link rows and
  external-link buttons reuse existing handlers and authoritative URI constants.
  Display URLs derive from those same URI objects. Version derives from the app
  assembly, not a hard-coded screenshot value. Empty copy feedback consumes no
  space; failures remain visible. No clipboard or browser/email client is invoked
  during automated verification.
- The window-only sharing limitation remains visible. Contextual keyboard/pointer
  help retains full-screen sharing limits, live-frame handling and the distinction
  between blur and complete concealment. Optional contact links explicitly use
  the user's configured email client/browser.

212 unit tests pass. Compiled WPF checks pass the existing 100–200% simulated DIP
matrix and About-specific 1280/1000/860-DIP transitions, email-text/action bounds,
contact routes/accessible names, frozen shield, help, feedback and non-mutating
navigation. Builds have zero warnings/errors; 17 PowerShell syntax checks,
installer transaction/six entrypoint cases and isolated single-file ZIP/checksum/
shortcut/install/uninstall smoke pass. Native WPF previews contain synthetic
settings, not protected app content. The installed user app/settings are untouched.

Real per-monitor DPI, keyboard/screen-reader announcements and native clipboard/
contact-client launch/failure checks remain live release gates; automated layout
and route checks are not evidence of those interactions. No new UI framework,
runtime capture loop, version bump, schema migration, tag or release is introduced.
Version remains unreleased 2.0.0 and schema 12.

## Prerelease identity follow-up

The subsequent version-policy change prepares `2.0.0-beta.1`, with numeric
FileVersion `2.0.0.1` and AssemblyVersion `2.0.0.0`. It supersedes earlier
stable-looking version labels in these historical phase notes. About/Settings
now retain prerelease labels from informational metadata; About version help
exposes full Git build metadata when present. Version changes do not migrate
schema 12 or alter capture, reveal, profiles or PIN behavior.

221 unit tests and 53 isolated version-policy checks pass, including mocked
execution of the actual release command and rejection of stale product/file/tag
identities. All 18 PowerShell syntax checks, solution build, compiled WPF matrix,
installer checks and isolated beta ZIP/checksum/install/uninstall smoke pass.
The workflow is prepared to publish prereleases as non-Latest on an existing
matching tag, but this work does not create any tag or publish a release. Live
qualification gates remain open. See [release-process.md](release-process.md).
