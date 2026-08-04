# Releases

KM Editor publishes Windows desktop builds through GitHub Releases.

## Release Workflow

The `Desktop Release` workflow requires its source SHA to be a two-parent merge whose tree exactly matches its second parent, verifies successful `Build / Desktop` and `Build / Backend` checks on that pull-request head, compiles the Tauri desktop app on Windows, packages it with the custom KM Editor setup, and uploads the release assets to a draft GitHub Release.

KM Editor 2.4.0 is the first release using the custom setup described below. KM Editor 2.3.6 is the final release using the legacy NSIS and MSI asset set.

The custom setup release assets are:

- `KM.Editor.Setup_<version>_x64.exe`, the custom install, update, and uninstall package
- The Tauri updater signature for that setup executable
- `latest.json`, which points every supported Windows updater identity at the same signed setup executable
- `SHA256SUMS.txt` for the uploaded assets

## Desktop Update Checks

The installed desktop app checks for a newer stable release after launch without interrupting startup. When an update is available, Settings shows a gold notice and changes its update action to install the native update or open the matching GitHub Release. Users can defer the prompt and return to the same action later.

Native updates use Tauri's updater plugin, the public key in `apps/desktop/src-tauri/tauri.conf.json`, and the `latest.json` asset attached to the latest published GitHub Release. Settings also keeps a manual check for users who want to retry or confirm that the installed version is current.

If native update checks are unavailable, Settings falls back to opening the newer GitHub Release page.

Users on versions before the native updater was added must manually install the first updater-enabled release. After that install, later releases can update natively.

## Updater Signing

Tauri updater artifacts must be signed. The release workflow expects these GitHub Actions secrets:

- `TAURI_SIGNING_PRIVATE_KEY`
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`, if the private key was created with a password

The private signing key must never be committed. If the private key or password is lost, installed updater-enabled builds cannot receive future native updates signed by a different key.

The workflow also requires repository variable `WIX_V7_EULA_ACCEPTED=true`, recorded only after the WiX v7 OSMF EULA has been reviewed and accepted. KM Editor's outer setup is not currently required to carry an Authenticode signature; Tauri/Minisign updater signing remains mandatory, and the included WebView2 bootstrapper must retain its valid Microsoft Authenticode signature.

## Manual Release

Use the GitHub Actions UI when a release should be created from the final pull request merge on `master`:

1. Open `Actions`.
2. Run `Desktop Release`.
3. Select `master` as the workflow branch.
4. Enter a tag such as `v0.1.0`.
5. Leave `prerelease` unchecked for normal releases.
6. Review the generated draft assets and replace the generated notes with the final changelog and comparison link.
7. Publish the completed draft release from GitHub.

For a manual run, `gh release create` creates the requested tag at the workflow SHA if it does not already exist. A tag-push run uses the tag that triggered it.

The tag must match the desktop app version and point to a pull request merge whose tree matches its build-checked head. For example, `v0.1.0` requires the app version to be `0.1.0`.

## Tag Release

Pushing an exact numeric `vX.Y.Z` tag also starts the release workflow. Other `v*` tags may trigger the job but are rejected by the version gate before packaging:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Create the tag at the final build-checked pull-request merge on `master`. The workflow rejects non-two-parent source commits, trees that differ from the second parent, and second parents without the required successful product checks; selecting and verifying `master` remains a maintainer responsibility.

## Version Checklist

Set the release version from the repository root before creating the release pull request. Replace `X.Y.Z` with the intended numeric version:

```powershell
pnpm version:set X.Y.Z
pnpm check:version X.Y.Z
```

`pnpm version:set` updates exactly six synchronized fields:

- the version in `package.json`
- the version in `apps/desktop/package.json`
- the version in `apps/desktop/src-tauri/tauri.conf.json`
- the main window title in `apps/desktop/src-tauri/tauri.conf.json`
- the package version in `apps/desktop/src-tauri/Cargo.toml`
- the unique `km-editor-desktop` package version in `apps/desktop/src-tauri/Cargo.lock`

`pnpm check:version X.Y.Z` fails unless all six fields match the requested version. The normal repository check and pull-request desktop build also run the synchronization check. The release workflow repeats it against the exact `vX.Y.Z` tag before packaging.

The version command does not update dependency versions, toolchain versions, supported game versions, protocol or manifest format versions, release tags, historical release statements, or release records. In particular, references identifying 2.3.6 as the final legacy-installer release remain historical facts.

The README release badge reads the latest published GitHub Release dynamically and has no literal app version to change. Review the release-facing README summary during release preparation, then verify that the badge and latest-release link resolve to the newly published release.

Desktop packaging derives the project bridge binary metadata from the synchronized app version. The setup driver derives setup binary metadata from its mandatory version and rejects manual packaging when that version differs from the synchronized source metadata or the staged application and project bridge metadata.

Use the same version number in the GitHub release tag, prefixed with `v`.
