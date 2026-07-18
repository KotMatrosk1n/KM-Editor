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

KM Editor uses .NET, React, TypeScript, pnpm, Rust, and Tauri. The exact supported versions and platform prerequisites are listed in the [README](README.md).

After cloning the repository, install the locked dependencies and restore the .NET solution:

```powershell
pnpm install --frozen-lockfile
dotnet restore .\KM.Editor.slnx
```

Start the complete desktop development environment with `pnpm tauri:dev`. The root `package.json` remains the source of truth for development and build scripts.

Run `pnpm check` before submitting a change. It checks tracked workspace paths, builds the desktop app, and builds the .NET solution. Pull requests run the same product build checks automatically.

## Make changes that are safe to review

Keep pull requests focused. Explain the user impact and root cause for a fix, or the full user workflow for a feature.

Game families have separate data formats and workflow services. Similar controls do not prove that the underlying fields behave the same way. Verify each game that a change claims to support.

For anything that writes or removes files:

* Treat base project paths as read only.
* Write generated data only under the selected output root.
* Preserve fields and files the current workflow does not own.
* Make cleanup and uninstall remove only output KM Editor can prove it owns.
* Fail safely when input structure, version, or ownership cannot be verified.

Avoid unrelated formatting or generated file churn. Do not commit local output, caches, build artifacts, scratch research, private fixtures, internal notes, local filesystem paths, credentials, signing material, or copyrighted assets.

External projects may be useful research references, but their source, namespaces, generated types, and comments do not automatically belong in KM Editor. Follow their licenses, document permitted provenance for maintainers, and use KM owned names in original project code.

## Verification and documentation

Verify the affected workflow with the relevant supported game and a disposable output folder. Check both the visible editor result and the files written by apply, restore, cleanup, or uninstall actions. Remove temporary probes, generated projects, and debugging artifacts after verification.

Update public documentation when behavior, supported fields, project setup, output ownership, or troubleshooting steps change. Public text should describe shipped behavior, not private research history or local development context.

## Pull request checklist

Before opening a pull request, make sure:

* The change is scoped and unrelated local work is excluded.
* Changed behavior was manually verified in the affected workflow.
* Temporary files, diagnostics, and private data are absent from the diff.
* Output ownership and cleanup behavior are explained when relevant.
* User documentation is updated when the workflow changed.
* The contribution is compatible with the project's [GPL 3.0 only license](LICENSE).

By submitting a contribution, you agree that it may be distributed under the repository's license.
