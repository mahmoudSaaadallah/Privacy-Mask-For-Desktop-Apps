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
