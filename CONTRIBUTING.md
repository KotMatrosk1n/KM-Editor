# Contributing to KM Editor

KM Editor supports Pokemon Sword and Shield, Pokemon Scarlet and Violet, and Pokemon Legends Z-A mod projects. Contributions are welcome across the desktop app, backend workflows, binary formats, documentation, localization, and issue reports.

The best contribution is not necessarily the largest one. A focused fix that preserves someone else's project is worth far more than a heroic rewrite that eats their output folder.

## Choose the right starting point

Before opening a new issue, search the existing issues and check the [wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki). Then choose the route that best matches the problem:

* **Bug Report** for editor failures, crashes, incorrect loading, or incorrect output.
* **In Game Behavior** when KM Editor wrote output successfully but the game behaved differently.
* **Feature Request** for a new workflow, field, format, or safety improvement.
* **Docs or Wiki** for unclear, missing, or outdated documentation.
* **Security Policy** for private reports involving trust boundaries, unsafe file access, private data, or release integrity.

Please reproduce bugs on the latest public release when possible. Older releases may already contain a fixed problem.

## Write a report someone can reproduce

A useful report includes:

* The KM Editor version and selected game.
* The editor or workflow involved.
* Whether the record came from the base project or an existing output override.
* The exact actions that led to the problem.
* What you expected and what happened instead.
* Any diagnostic code or message shown by KM Editor.
* Whether the problem happens with a clean output folder, when that test is safe and practical.

Screenshots are useful when they show the relevant control, value, or error. Crop out account names, local paths, and unrelated applications first.

Do not upload game dumps, executable files, copyrighted game assets, private saves, credentials, signing keys, access tokens, personal data, or complete generated mod packages. Usually a file name, diagnostic message, and short description are enough to start an investigation.

## Feature requests

Start with the player visible or editor visible goal. Explain what you are trying to change, where the result appears, and why the current workflow falls short.

Known file names, fields, small byte patterns, public technical references, and screenshots can help. Clearly label confirmed behavior and guesses. Do not include material copied from another project unless its license permits reuse and the contribution records that provenance appropriately.

## Development setup

KM Editor uses .NET, React, TypeScript, pnpm, Rust, and Tauri. The supported .NET SDK is defined in [`global.json`](global.json), Node.js and pnpm requirements are defined in [`package.json`](package.json), and the Rust requirement is defined in [`Cargo.toml`](apps/desktop/src-tauri/Cargo.toml). Windows desktop development also requires Visual Studio 2022 Build Tools with Desktop development with C++, a Windows 10 or 11 SDK, and Microsoft Edge WebView2 Runtime.

After cloning the repository, install the locked dependencies and restore the .NET solution:

```powershell
pnpm install --frozen-lockfile
dotnet restore .\KM.Editor.slnx
```

Start the complete desktop development environment with `pnpm tauri:dev`. The root `package.json` remains the source of truth for development and build scripts.

Run `pnpm check` before submitting a change. It checks tracked workspace paths, builds the desktop app, and builds the .NET solution. Pull requests run those product builds and also compile the native desktop shell with locked Cargo dependencies.

### Desktop interface contract

Every new desktop editor, field, button, and composite control must use the shared KM interface
theme. A feature stylesheet may control layout and density, but it must not replace shared control
colors, native affordances, interaction feedback, or accessibility states with browser defaults,
platform gray, inline styles, or hardcoded colors.

The complete implementation rules and acceptance checklist are in the
[desktop app contributor guide](apps/desktop/README.md#km-interface-contract). Desktop typecheck
recursively enforces this contract across TSX and CSS, so run `pnpm typecheck` while developing UI
changes rather than waiting for pull request validation.

## Make changes that are safe to review

Keep pull requests focused. Explain the user impact and root cause for a fix, or the full user workflow for a feature.

Game families have separate data formats and workflow services. Similar controls do not prove that the underlying fields behave the same way. Verify each game that a change claims to support.

For anything that writes or removes files:

* Treat base project paths as read only.
* Write generated data only under the selected output root.
* Preserve fields and files the current workflow does not own.
* Preserve untouched raw fields behind inactive sentinels such as an empty species or item ID. A read model may present an inactive row as empty, but output must not erase latent bytes unless the user explicitly edits or clears that row.
* Canonicalize parent and dependent edits at every session boundary. Removing or restoring an identity field must not leave detail edits that can block validation later, even when a session was restored, reordered, or supplied directly to the bridge.
* Resolve player facing labels through authoritative catalogs. Internal event IDs, row indexes, and storage keys belong in technical provenance and must not be presented as public mission or record numbers.
* Make cleanup and uninstall remove only output KM Editor can prove it owns.
* Fail safely when input structure, version, or ownership cannot be verified.

Avoid unrelated formatting or generated file churn. Do not commit local output, caches, build artifacts, scratch research, private fixtures, internal notes, local filesystem paths, credentials, signing material, or copyrighted assets.

External projects may be useful research references, but their source, namespaces, generated types, and comments do not automatically belong in KM Editor. Follow their licenses, document permitted provenance for maintainers, and use KM owned names in original project code.

## Verification and documentation

Verify the affected workflow with the relevant supported game and a disposable output folder. Check both the visible editor result and the files written by apply, restore, cleanup, or uninstall actions. Remove temporary probes, generated projects, and debugging artifacts after verification.

Temporary local tests are welcome when they help verify a change. Submitted diffs should not add tracked test projects, fixtures, runners, result files, or test-only dependencies.

Update public documentation when behavior, supported fields, project setup, output ownership, or troubleshooting steps change. Public text should describe shipped behavior, not private research history or local development context.

## Pull request checklist

Before opening a pull request, make sure:

* The change is scoped and unrelated local work is excluded.
* Changed behavior was manually verified in the affected workflow.
* Temporary files, diagnostics, and private data are absent from the diff.
* Output ownership and cleanup behavior are explained when relevant.
* New desktop UI follows the KM interface contract and passes its static control-theme checks.
* User documentation is updated when the workflow changed.
* The contribution is compatible with the project's [GPL 3.0 only license](LICENSE).

By submitting a contribution, you agree that it may be distributed under the repository's license.
