# Sidebar and settings edit session

The sidebar selects Overview, Applications, Shortcuts, Security, Settings,
or About. These views share one settings view model and are not reconstructed
when the user changes pages. Navigating commits the active text/grid editor;
invalid editor values must be corrected before changing pages.

The footer reports unsaved changes, saving, saved state, or a failed save. Save
persists the complete edit session and keeps the current page open. Discard
restores saved values across all pages and keeps the selected page open.
Closing the window or pressing Escape retains the previous discard-to-tray
behavior. Ctrl+S saves without closing the window.

Pending-change tracking compares the persisted settings graph with a saved
baseline. It observes startup choices, shortcut enablement, profile changes,
add/remove/relink operations, replacement zone collections, every editable zone
field, and PIN credentials. Navigation, derived labels, and runtime status
updates are excluded. Returning a value to its saved state clears its pending
change. Replaced sessions detach property and collection subscriptions.

Valid changes request a coalesced live preview on the WPF dispatcher. Invalid
binding values or validation failures do not replace the last valid preview.
Runtime shortcut enrollment and tray changes retain their immediate-save
behavior; their saved baseline is synchronized without discarding unrelated
pending edits or requesting another preview solely because the baseline moved.

This stage provides page navigation and edit-session behavior. Later GUI PRs
replace the existing application forms with compact cards/details and complete
the reviewed Overview, Security, and inline About layouts.

Validation includes eight settings-session tests and the existing suite (180
tests total), loading and rendering all six compiled WPF pages, and exercising
the real Save/Discard handlers with an in-memory save callback. The isolated
render check does not start the user's overlays or modify their settings.
