# Releases

KM Editor publishes Windows desktop builds through GitHub Releases.

## Release Workflow

The `Desktop Release` workflow builds the Tauri desktop app on Windows, runs backend and desktop tests, packages the app, and creates a draft GitHub Release.

The release assets are:

- NSIS setup executable
- MSI installer
- Signed Tauri updater bundle ZIPs and signatures when updater artifacts are configured
- `SHA256SUMS.txt` for the uploaded assets

## Desktop Update Checks

The desktop app can check GitHub Releases from Settings. When a newer published release is available, it prefers signed Tauri updater bundle ZIP assets such as `.nsis.zip` or `.msi.zip`. If a release only has full installers, the app opens the GitHub release page instead of directly downloading the setup executable.

## Manual Release

Use the GitHub Actions UI when a release should be created from the current branch:

1. Open `Actions`.
2. Run `Desktop Release`.
3. Enter a tag such as `v0.1.0`.
4. Leave `prerelease` unchecked for normal releases.
5. Review the generated draft release notes and assets.
6. Publish the draft release from GitHub.

The workflow creates the tag at the commit that ran the workflow if the tag does not already exist.

The tag must match the desktop app version. For example, `v0.1.0` requires the app version to be `0.1.0`.

## Tag Release

Pushing a version tag also starts the release workflow:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## Version Checklist

Before creating a release, update the desktop app version in:

- `apps/desktop/src-tauri/tauri.conf.json`
- `apps/desktop/src-tauri/Cargo.toml`

Use the same version number in the GitHub release tag, prefixed with `v`.
