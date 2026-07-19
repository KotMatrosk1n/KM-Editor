# Releases

KM Editor publishes Windows desktop builds through GitHub Releases.

## Release Workflow

The `Desktop Release` workflow verifies that the exact pull request merge passed the required product builds, builds the Tauri desktop app on Windows, packages it, and creates a draft GitHub Release.

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

Use the GitHub Actions UI when a release should be created from the final pull request merge on `master`:

1. Open `Actions`.
2. Run `Desktop Release`.
3. Select `master` as the workflow branch.
4. Enter a tag such as `v0.1.0`.
5. Leave `prerelease` unchecked for normal releases.
6. Review the generated draft release notes and assets.
7. Publish the draft release from GitHub.
8. Replace the generated notes with the final changelog and comparison link.

The workflow creates the tag at the commit that ran the workflow if the tag does not already exist.

The tag must match the desktop app version and point to a pull request merge whose tree matches its build-checked head. For example, `v0.1.0` requires the app version to be `0.1.0`.

## Tag Release

Pushing a version tag also starts the release workflow:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Create the tag at the final build-checked pull request merge on `master`. Direct release commits, mismatched merge trees, and tags on source without successful product builds are rejected before packaging.

## Version Checklist

Before creating a release, update the desktop app version in:

- `package.json`
- `apps/desktop/package.json`
- the version and main window title in `apps/desktop/src-tauri/tauri.conf.json`
- `apps/desktop/src-tauri/Cargo.toml`
- the `km-editor-desktop` package entry in `apps/desktop/src-tauri/Cargo.lock`

Review the README release badge and release-facing feature summary at the same time.

Use the same version number in the GitHub release tag, prefixed with `v`.
