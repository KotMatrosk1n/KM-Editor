# Releases

KM Editor publishes Windows desktop builds through GitHub Releases.

## Release Workflow

The `Desktop Release` workflow builds the Tauri desktop app on Windows, runs backend and desktop tests, packages the app, and creates a draft GitHub Release.

The release assets are:

- NSIS setup executable
- MSI installer
- Tauri updater signatures for the Windows installers
- `latest.json`, which points native update checks at the signed Windows installer
- `SHA256SUMS.txt` for the uploaded assets

## Desktop Update Checks

The desktop app can check for native updates from Settings. Native updates use Tauri's updater plugin, the public key in `apps/desktop/src-tauri/tauri.conf.json`, and the `latest.json` asset attached to the latest published GitHub Release.

If native update checks are unavailable, Settings falls back to opening the newer GitHub Release page.

Users on versions before the native updater was added must manually install the first updater-enabled release. After that install, later releases can update natively.

## Updater Signing

Tauri updater artifacts must be signed. The release workflow expects these GitHub Actions secrets:

- `TAURI_SIGNING_PRIVATE_KEY`
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`, if the private key was created with a password

The private signing key must never be committed. If the private key or password is lost, installed updater-enabled builds cannot receive future native updates signed by a different key.

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

- `package.json`
- `apps/desktop/package.json`
- `apps/desktop/src-tauri/tauri.conf.json`
- `apps/desktop/src-tauri/Cargo.toml`

Use the same version number in the GitHub release tag, prefixed with `v`.
