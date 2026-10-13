# PrivacyMask brand assets

The approved private-view mark uses a white eye, a teal diagonal slash, and a
navy rounded-square background. Its editable source is
`src/PrivacyMask.App/Assets/Brand.xaml`. The same vector can be used as the
`PrivacyMaskLogo` drawing resource in WPF pages.

Regenerate the checked-in PNG and ICO on Windows using:

```powershell
powershell -STA -ExecutionPolicy Bypass -File .\scripts\generate-brand-icon.ps1
```

The ICO contains alpha-preserving PNG frames at 16, 20, 24, 32, 48, 64, 128,
and 256 pixels. Generated assets are committed so ordinary builds do not need
an asset generation step or additional dependencies. The PNG is a 256-pixel
preview. The executable embeds the ICO; installed shortcuts reference that
executable's first icon, and the tray owns an independent icon copy disposed
after the notification icon.

The GUI palette and keyed component styles live in
`src/PrivacyMask.App/Styles/DesignSystem.xaml`. Existing controls retain their
legacy button and checkbox defaults until each page adopts the new styles.
Text color inherits from the containing window/control rather than an implicit
TextBlock style, so buttons can use their own readable foreground.

This stage remains part of the unreleased 2.0.0 product version and does not
change settings schema 12. Future page titles must use the existing assembly
version source.

Validation includes compiled WPF resource loading, constructing the settings
window without starting overlays or touching settings, rendering the shared
controls, checking text foreground inheritance, and loading the tray-size ICO
frame. Installer and release-package smoke tests verify the shortcut icon
reference on installation and update. The 172 existing tests and solution
build also pass.

The new page layouts and keyboard tooltip behavior are delivered by later
GUI PRs. Small-size visual inspection and actual tray appearance remain part
of the final GUI qualification; an embedded icon alone does not validate every
Windows display scale or shell-cache state.
