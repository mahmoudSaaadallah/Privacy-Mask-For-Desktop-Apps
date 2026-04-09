# Release Process

## Local release steps

1. Run `scripts/verify.ps1`
2. Run `scripts/publish-win-x64.ps1`
3. Review the output in `desktop-app/windows/win-x64/app`
4. Commit source changes and release artifacts
5. Push to GitHub

## Recommended future improvements

- Publish signed builds through GitHub Releases
- Add an MSI or MSIX installer
- Add smoke-test automation for published builds
