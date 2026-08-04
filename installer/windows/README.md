<!-- SPDX-License-Identifier: GPL-3.0-only -->

# KM Editor Windows Setup

This directory contains KM Editor's custom Windows setup implementation. It is intentionally
separate from the normal KM Editor solution and pull-request build workflow so ordinary
application compilation does not create installer artifacts. Release packaging is wired
separately through `.github/workflows/desktop-release.yml`, which invokes the setup driver
for eligible versioned release runs.

KM Editor 2.4.0 is the first release using the custom setup documented here. KM Editor
2.3.6 is the final release using the legacy NSIS and MSI asset set.

## Architecture

The setup executable produced by current source is a small native updater-compatibility
launcher containing an exact hash-pinned WiX Burn bundle. The launcher validates and starts
that bundle; it never owns installation state. The self-contained WPF bootstrapper
application owns presentation only. Burn and Windows Installer own detection, planning,
caching, progress, cancellation, elevation, rollback, repair, update, and uninstall
behavior.

```text
KM.Editor.Setup_<version>_x64.exe
                        published native compatibility launcher
  -> WiX Burn           cached lifecycle, update, repair, and uninstall owner
       -> KM.Setup.UI   custom WPF bootstrapper application
       -> KM.Setup.Package
            -> KM Editor desktop executable and bundled project bridge
```

The MSI defaults to a per-user installation. A previously installed per-machine MSI stays
per-machine during upgrade so existing installations do not silently change ownership or
scope.

Shortcut choices are written by the MSI as explicit `0` or `1` values in its selected
HKCU or HKLM product scope. Every related bundle reads that stable state before showing
options or planning a silent update; Burn registration-private persistence is not used as
cross-version storage.

## Projects

- `KM.Setup.UI`: KM-themed WPF window, setup state model, Burn event adapter, updater
  argument compatibility, and user-visible error reporting.
- `KM.Setup.Package`: Windows Installer package for the desktop executable, project bridge,
  shortcuts, registry identity, upgrades, repair, and removal.
- `KM.Setup.Bundle`: Persistent Burn bundle that is the canonical install, update, repair,
  and uninstall owner.
- `KM.Setup.Launcher`: Statically linked x64 wrapper that preserves Tauri's legacy updater
  arguments, hash-verifies the embedded Burn bundle, and forwards its exact exit code.

These projects are not added to `KM.Editor.slnx`. A normal backend or desktop compile, and
the pull-request `Build` workflow, must not create Windows installer artifacts. This does
not exclude setup from releases: the dedicated `Desktop Release` workflow builds the
unbundled desktop application and then invokes the setup driver explicitly.

WiX v7 is required for configurable bundle scope. Its OSMF EULA requires an explicit
maintainer acceptance gesture, so these projects deliberately do not set `AcceptEula` on
the user's behalf. Local packaging requires `-AcceptWixEula`; GitHub release packaging
requires the repository variable `WIX_V7_EULA_ACCEPTED=true`.

## Build inputs

Production packaging requires exact release inputs:

- the unbundled Tauri executable, named `km-editor-desktop.exe`;
- the self-contained project bridge, staged as `km-tools-bridge.exe`;
- the KM application icon;
- the Microsoft-signed WebView2 Evergreen Bootstrapper; and
- an explicit KM version supplied by the release process.

Installer projects must fail when a required input is absent. They must never silently use
an old executable, infer a release version from an output filename, or download an
unverified executable during compilation. Manual packaging also fails when its requested
version differs from synchronized repository metadata or from the staged application and
project bridge binary metadata.

`scripts/Build-KmWindowsSetup.ps1` is the only packaging driver. It accepts the Cargo target
directory containing the exact release application, plus exact sidecar, WebView2, version,
and output paths; stages everything under a unique ignored working directory; builds the
MSI, custom BA, Burn bundle, and native launcher in order; copies only the final outer EXE
plus a build receipt to the requested output directory; and removes that exact GUID-scoped
working directory. `-KeepIntermediates` retains it for diagnostics. The driver never
launches or installs the result.

The driver requires an explicit `-AcceptWixEula` switch and passes WiX's non-persistent
`AcceptEula=wix7` build property. Omitting the switch fails before restore or compilation.
Every build produced by this driver enables the verified legacy-installation takeover with
the same production identity used for clean installs and updates.

The staged project bridge derives its assembly, file, informational, and product version
metadata from the synchronized KM Editor app version. The setup driver supplies that same
version to the custom setup UI and native launcher. This keeps the installed application,
project bridge, setup display, Windows package registration, artifact name, and updater
metadata on one release version without adding independent version literals.

The staging script patches exactly one Tauri bundle marker from `UNK` to `NSS` in the
staged copy of the application before it is signed. This intentionally keeps all future
custom-installer builds on Tauri's established `windows-x86_64-nsis` updater-family key;
the source build output is never modified.

## Release version synchronization

Set and verify a release version from the repository root before the release pull request.
Replace `X.Y.Z` with the intended numeric version:

```powershell
pnpm version:set X.Y.Z
pnpm check:version X.Y.Z
```

The set command updates exactly six fields:

- the root package version;
- the desktop package version;
- the Tauri application version;
- the Tauri main window title;
- the desktop Cargo package version; and
- the unique desktop package entry in `Cargo.lock`.

The check command requires all six fields to match. Pull-request desktop builds run the
same synchronization check, and the release workflow checks them again against the exact
numeric release tag before it invokes this setup driver.

These commands do not change dependency or toolchain versions, supported game versions,
installer family identities, protocol and manifest format versions, release tags, or
historical release documentation. The statement that 2.3.6 is the final legacy-installer
release is intentionally retained. The README release badge has no hard-coded application
version; it follows the latest published GitHub Release and should be verified after that
release is published.

## GitHub release automation

The pull-request `Build` workflow compiles and checks the product but does not package an
installer. The `Desktop Release` workflow is the only GitHub Actions caller of the setup
driver. It starts for a pushed `v*` tag or an explicit manual run, then rejects the run
before packaging unless all of these conditions hold:

- the release tag is exactly numeric `vX.Y.Z` and every desktop version field matches it;
- the source is a two-parent pull-request merge whose tree matches its checked head;
- `Build / Desktop` and `Build / Backend` passed for that head;
- repository variable `WIX_V7_EULA_ACCEPTED` is `true`; and
- Actions secret `TAURI_SIGNING_PRIVATE_KEY` is configured. The password secret is needed
  only when that updater-signing key is password-protected.

An eligible run builds the unbundled application, downloads and verifies the current
Microsoft-signed WebView2 bootstrapper, invokes the setup driver, and Minisign-signs the
final outer launcher for Tauri updater verification. It creates a **draft** GitHub Release
containing exactly these four assets:

```text
KM.Editor.Setup_<version>_x64.exe
KM.Editor.Setup_<version>_x64.exe.sig
latest.json
SHA256SUMS.txt
```

The local build receipt, inner Burn executable, and MSI are not published. A maintainer
reviews and publishes the draft; pushing an ordinary commit or opening a pull request never
creates a setup artifact or release. KM Editor's own setup is not currently required to be
Authenticode-signed. The bundled WebView2 prerequisite must retain its valid Microsoft
Authenticode signature, and the outer setup must have its Tauri/Minisign updater signature.

## Progress contract

The progress bar is driven only by Burn engine events. Detection and planning use an
indeterminate state because no honest completion percentage exists during those phases.
During apply, `OverallPercentage` is shown directly. A success page is entered only after a
successful `ApplyComplete` result.

No timer, scripted animation, control-range override, or cosmetic assignment may synthesize
determinate progress. The visual animation used while Burn is indeterminate carries no
percentage claim.

## Updater compatibility

Existing KM Editor versions use Tauri's Windows updater and may launch any downloaded EXE
with the legacy NSIS-style arguments `/P`, `/R`, `/UPDATE`, and `/ARGS`. The custom
bootstrapper accepts that contract while also supporting normal Burn command-line modes.

- `/P` requests the themed passive progress window.
- `/R` requests a successful post-update relaunch.
- `/UPDATE` selects the noninteractive upgrade path.
- `/ARGS` preserves arguments from the previous KM Editor process.

Tauri's `/R` requests an application relaunch; it is not permission to reboot Windows.
For an in-app update, the compatibility launcher therefore adds Burn's `-norestart`
unless an explicit restart mode was supplied. Direct setup supports `-norestart`,
`-promptrestart`, and `-forcerestart`. A full interactive setup prompts with a themed
Restart now action when Windows Installer reports that a reboot is required. Passive and
quiet modes follow Burn's explicit restart policy and return standard Windows Installer
codes `3010` (restart required) or `1641` (restart initiated).

Burn interprets its own switches before a bootstrapper application starts, including
switch-looking values that Tauri places after `/ARGS`. The custom setup therefore
uses a small native compatibility launcher in front of the Burn bundle. That launcher
treats the `/ARGS` tail as opaque application arguments and passes it to Burn only through
a hidden encoded variable. The launcher is not an installation owner and never appears in
Apps & Features; the cached Burn bundle remains the sole setup owner.

The final outer launcher is Minisign-verified by the application before execution. The
launcher also pins and verifies the exact embedded Burn payload with SHA-256 before
execution. An unsigned embedded payload is accepted, while any present signature must be
structurally valid and trusted.

The existing application-side update checker, endpoint, public key, and passive install
mode remain the integration point. The release workflow generates `latest.json` with all
three Windows identities pointing at the same final launcher bytes and Minisign signature:

```text
windows-x86_64-nsis -> KM.Editor.Setup_<version>_x64.exe
windows-x86_64-msi  -> KM.Editor.Setup_<version>_x64.exe
windows-x86_64      -> KM.Editor.Setup_<version>_x64.exe
```

Only the outer launcher is an updater artifact. The inner Burn EXE and MSI are never
published as alternate user-facing installers. Installer packaging is deliberately not run
by the pull-request `Build` workflow. Eligible pushed numeric version tags and eligible
manual `Desktop Release` runs build the custom setup and Minisign-sign only the final outer
bytes. Maintainers may also invoke the packaging driver explicitly for local release
validation; ordinary application and solution builds never invoke it. All three Windows
updater identities reference that same EXE and signature.

The release job requires repository variable `WIX_V7_EULA_ACCEPTED=true` after the WiX v7
OSMF EULA has been reviewed and accepted, plus the `TAURI_SIGNING_PRIVATE_KEY` Actions
secret and `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` when that key is password-protected. Legacy
migration is part of every production setup and is not controlled by a repository variable.
The integration accepts numeric `vX.Y.Z` release tags only; prerelease status remains a
GitHub Release flag rather than an MSI version suffix.

## Legacy installation migration

The bundle must recognize both published installation families:

- current-user NSIS installations registered under the exact KM Editor uninstall key; and
- per-machine MSI installations related by KM Editor's established MSI upgrade code.

Migration retires legacy registration inside the same rollback-backed MSI transaction that
installs the new owned files. A failed transaction restores the previous state. The final
state must contain one application installation and one Apps & Features owner.

The source includes an exact-file NSIS takeover inside the MSI transaction. After the BA
matches the published identity, version, two install-location records, uninstaller path,
local fixed-drive policy, and single-owner state, Windows Installer removes only the two
known application binaries, `uninstall.exe`, the two known shortcuts, and the legacy Apps
& Features key before installing the new owned copies. Standard MSI actions record those
deletions in the rollback script. A separate fixed-path lifecycle clears only rebuildable
`projects` and `tmp` cache directories on install, update, and uninstall while preserving
cache settings and application data. Full uninstall can also remove those settings and the
application data roots when the user explicitly selects that option.

The production packaging driver always enables this takeover. A registered Burn installation
can still uninstall itself when a stale legacy key or MSI ownership conflict is present.

## Release gate

Do not replace the published installer or updater manifest until clean install, update,
repair, uninstall, cancellation, rollback, legacy migration, passive relaunch, app-in-use,
and display-scaling checks pass on supported Windows versions. Installer packaging and
installation are explicit release-validation actions, not normal pull-request side effects.
This lifecycle matrix is a manual release gate. GitHub Actions validates the source, product
checks, versions, packaging inputs, and updater signature, but it does not automate every
Windows installation scenario listed above.
