# PrivacyMask 2.0: Custom Application Support

PrivacyMask 2.0 expands protection from the built-in WhatsApp and Telegram
profiles to any user-selected desktop application. The work is deliberately
split into reviewable stages so settings compatibility, matching safety, and
resource usage remain measurable throughout the change.

## Product goals

- Let the user choose a currently running application and add it without
  typing a process name.
- Give every added application its own name, enabled state, appearance,
  strength, reveal size, and full-window protection zone.
- Support multiple user-added applications, including more than one custom
  profile of the same application type.
- Preserve the existing WhatsApp and Telegram profiles and migrate all 1.x
  settings without losing user choices.
- Keep matching process-scoped and local. PrivacyMask does not inject code,
  read window contents semantically, or persist captured frames.

## Safety boundaries

The application picker will expose only visible top-level windows that have a
resolvable executable process. PrivacyMask itself, shell windows, and known
security-sensitive system surfaces will not be offered. A custom profile uses
an exact process-name match so selecting `Notepad` cannot unintentionally match
another process whose name merely starts with the same text.

New profiles start with a single full-window zone. This is a safe and useful
default for an unknown application's layout; users can refine zones later
through the existing advanced editor.

## Delivery stages

### 1. Profile and runtime foundation

- Introduce stable profile identifiers that are independent of the built-in
  application enum.
- Add a custom application profile type and a factory for safe defaults.
- Match custom processes exactly through a generic Windows adapter.
- Resolve hotkey changes by profile identifier so multiple custom profiles do
  not collide.
- Upgrade settings schema to version 8 and normalize legacy 1.x settings.
- Verify cloning, validation, persistence, matching, resolution, and migration.

### 2. Running-application picker and profile management

- Add an **Add application** action to the settings window.
- List eligible running applications with their icon, friendly name, process
  name, and window title.
- Prevent duplicate additions and explain why an ineligible window is hidden.
- Add, rename, disable, reconfigure, and remove custom profiles without
  affecting built-in profiles.
- Refresh protection immediately after a profile is added or removed.

### 3. Usability and recovery

- Update onboarding and empty states to explain custom application protection.
- Provide actionable states when an added application is not running or its
  executable name changes after an update.
- Add confirmation and undo-friendly behavior for profile removal.
- Update installation, upgrade, and troubleshooting documentation for 2.0.

### 4. Release qualification

- Exercise the complete flow against representative Win32, WPF, and packaged
  desktop applications.
- Measure idle and active CPU/memory against the 1.1 baseline with several
  custom profiles configured.
- Verify 1.x-to-2.0 settings migration, package installation, in-place update,
  shortcut creation, rollback, and uninstall.
- Publish the signed-off `2.0.0` package only after the picker and management
  experience pass these checks.
