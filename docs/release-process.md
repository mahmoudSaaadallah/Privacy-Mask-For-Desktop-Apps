# Release Process

Generated Windows executables are distributed through GitHub Releases. They are
not committed to the source repository.

## Create and test a local release package

1. Run `scripts/verify.ps1`.
2. Run `scripts/package-win-x64-release.ps1`.
3. Extract and inspect `artifacts/release/PrivacyMask-win-x64.zip`.
4. Run `Install-PrivacyMask.cmd` from the extracted package.
5. Confirm the app launches from both generated shortcuts.
6. Run `Uninstall-PrivacyMask.cmd` and confirm the app and shortcuts are removed.

The packaging script publishes the self-contained `win-x64` single-file build,
includes the installer and uninstaller, and writes a SHA-256 checksum beside the
ZIP.

## Publish a GitHub release

1. Merge the fully verified release changes into `main`.
2. Create an annotated version tag such as `v1.1.0` on the release commit.
3. Push the tag to GitHub.
4. The `release` workflow builds and tests the project, creates the Windows ZIP
   and checksum, and publishes both files on the matching GitHub Release.
5. Download the published ZIP and perform the install/uninstall smoke test.

Example tag commands:

```powershell
git tag -a v1.1.0 -m "PrivacyMask v1.1.0"
git push origin v1.1.0
```

## Future improvements

- Sign release executables and installers
- Add an MSI or MSIX installer
- Add smoke-test automation for published builds
