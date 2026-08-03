<!-- SPDX-License-Identifier: GPL-3.0-only -->

# KM Editor Windows Setup

This directory contains KM Editor's custom Windows setup implementation. It is intentionally
separate from the normal KM Editor solution so ordinary application compilation does not
create installer artifacts.

## Architecture

The published setup executable is a small native updater-compatibility launcher containing
an exact hash-pinned WiX Burn bundle. The launcher validates and starts that bundle; it never
owns installation state. The self-contained WPF bootstrapper application owns presentation
only. Burn and Windows Installer own detection, planning, caching, progress, cancellation,
elevation, rollback, repair, update, and uninstall behavior.

```text
KM Editor Setup.exe     native compatibility launcher
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

These projects are not added to `KM.Editor.slnx`. A normal backend or desktop compile must
not create Windows installer artifacts.

WiX v7 is required for configurable bundle scope. Its OSMF EULA requires an explicit
maintainer acceptance gesture, so these projects deliberately do not set `AcceptEula` on
the user's behalf. That legal/release decision must be made before the first WiX build.

## Build inputs

Production packaging requires explicit, already-built inputs:

- the unbundled Tauri executable, named `km-editor-desktop.exe`;
- the self-contained project bridge, staged as `km-tools-bridge.exe`;
- the KM application icon;
- the Microsoft-signed WebView2 Evergreen Bootstrapper; and
- an explicit KM version supplied by the release process.

Installer projects must fail when a required input is absent. They must never silently use
an old executable, infer a release version from an output filename, or download an
unverified executable during compilation.

`scripts/Build-KmWindowsSetup.ps1` is the only packaging driver. It accepts exact app,
sidecar, WebView2, version, and output paths; stages everything under a unique ignored
working directory; builds the MSI, custom BA, Burn bundle, and native launcher in order;
copies only the final outer EXE plus a build receipt to the requested output directory; and
removes that exact GUID-scoped working directory. `-KeepIntermediates` retains it for
diagnostics. The driver never launches or installs the result.

The driver requires an explicit `-AcceptWixEula` switch and passes WiX's non-persistent
`AcceptEula=wix7` build property. Omitting the switch fails before restore or compilation.
Every build produced by this driver enables the verified legacy-installation takeover with
the same production identity used for clean installs and updates.

The staging script patches exactly one Tauri bundle marker from `UNK` to `NSS` in the
staged copy of the application before it is signed. This intentionally keeps all future
custom-installer builds on Tauri's established `windows-x86_64-nsis` updater-family key;
the source build output is never modified.

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
switch-looking values that Tauri places after `/ARGS`. The published executable therefore
uses a small native compatibility launcher in front of the Burn bundle. That launcher
treats the `/ARGS` tail as opaque application arguments and passes it to Burn only through
a hidden encoded variable. The launcher is not an installation owner and never appears in
Apps & Features; the cached Burn bundle remains the sole setup owner.

The final outer launcher is Minisign-verified by the application before execution. The
launcher also pins and verifies the exact embedded Burn payload with SHA-256 before
execution. An unsigned embedded payload is accepted, while any present signature must be
structurally valid and trusted.

The application-side update checker, endpoint, public key, and passive install mode do not
need a functional rewrite. At release cutover, the generated `latest.json` must point all
three Windows identities at the same final launcher bytes and Minisign signature:

```text
windows-x86_64-nsis -> KM Editor Setup.exe
windows-x86_64-msi  -> KM Editor Setup.exe
windows-x86_64      -> KM Editor Setup.exe
```

Only the outer launcher is an updater artifact. The inner Burn EXE and MSI are never
published as alternate user-facing installers. Installer packaging is deliberately not run
on normal pull requests. The tag-only desktop release job builds the custom setup and
Minisign-signs only the final outer bytes. All three Windows updater identities reference
that same EXE and signature.

The release job requires `WIX_V7_EULA_ACCEPTED=true` after the WiX v7 OSMF EULA has been
reviewed and accepted. Legacy migration is part of every production setup and is not
controlled by a repository variable. The integration accepts numeric `vX.Y.Z` release tags
only; prerelease status remains a GitHub Release flag rather than an MSI version suffix.

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
