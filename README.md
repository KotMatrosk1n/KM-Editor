# KM Editor

[![Latest release](https://img.shields.io/github/v/release/KotMatrosk1n/KM-Editor?label=release)](https://github.com/KotMatrosk1n/KM-Editor/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UK%20%7C%20ZH-orange)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)](LICENSE)

KM Editor is a Windows desktop editor for Pokemon Sword and Shield, Pokemon Scarlet and Violet, and Pokemon Legends Z-A mod projects.

It turns game records into searchable editors, keeps clean base files separate from generated output, and lets you review a complete change plan before anything is written. Spend more time making the mod and less time wondering which file just betrayed you.

[Download the latest release](https://github.com/KotMatrosk1n/KM-Editor/releases/latest) | [Read the wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) | [Report an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose)

## Start a Project

1. Install the latest Windows release.
2. Choose the exact game you are editing.
3. Select clean Base RomFS and Base ExeFS folders, then choose a separate Output Root.
4. Press **Validate Paths**.
5. Open **Workflows**, choose an editor, and stage edits into the current session.
6. Open **Changes**, choose **Review**, inspect the target files, and choose the output action for that game.

KM Editor does not include game files. Regular users do not need .NET, Node.js, Rust, Git, or a separate backend installation.

Changing a project path requires validation so every editor reloads from the newly selected project. **Switch and Revert** discard the draft or pending session named by the warning while keeping the current validated project open.

## Supported Workflows

| Game family | Editing coverage | Advanced tools | Guide |
| --- | --- | --- | --- |
| Sword and Shield | Pokemon, Rental Pokemon, moves, items, trainers, text, wild and static encounters, gifts, trades, raids, shops, rewards, and placement | ExeFS and hook based editors, Randomizer, Mod Merger, Game Dump, and Dump Importer | [Sword and Shield overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Overview) |
| Scarlet and Violet | Pokemon, moves, items, trainers, text, wild and static encounters, gifts, trades, Tera Raids, shops, and placement | Type Chart, Fashion Unlock, Hyperspace Bypass, data cache, Mod Merger, Game Dump, and Dump Importer | [Scarlet and Violet overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Overview) |
| Legends Z-A | Pokemon, moves, items, trainers, text, wild and static encounters, gifts, trades, shops, and placement | Type Chart, data cache, Mod Merger, Game Dump, and Dump Importer | [Legends Z-A overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview) |

The game guides are the authoritative feature maps. Similar editor names do not mean the games share formats or output rules.

The searchable Workflows page lists supported editor workflows for the selected game. Standalone tools and utility pages remain in the sidebar, which is the complete catalogue. Supported workflows and Settings open their matching wiki guides from inside the app.

## Built Around Reviewable Output

- Base RomFS and ExeFS remain read only.
- Existing Output Root files layer over the clean base, with source labels showing which record KM Editor loaded.
- Normal edits are grouped by editor in Changes. Individual staged rows can be removed before Review builds the complete output plan.
- ExeFS and hook based workflows use their own reviewed plans when they need coordinated file ownership.
- Scarlet and Violet plus Legends Z-A use separate bounded caches that can be cleared without deleting pending work.
- Project changes cancel stale loading work so results from one game or Output Root cannot replace another project.

A valid edit can still conflict with another mod when both replace the same file. Review the Output Plan and use the appropriate Mod Merger where one is available.

## Documentation

| Need | Start here |
| --- | --- |
| Set up paths and output | [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup) |
| Understand drafts, pending changes, and output | [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow) |
| Configure updates, data caches, or interface language | [Settings](https://github.com/KotMatrosk1n/KM-Editor/wiki/Settings) |
| Install, update, or uninstall KM Editor | [Installing and Updating](https://github.com/KotMatrosk1n/KM-Editor/wiki/Installing-and-Updating) |
| Fix a game-specific problem | [Sword and Shield](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Troubleshooting), [Scarlet and Violet](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Troubleshooting), or [Legends Z-A](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Troubleshooting) troubleshooting |
| Find technical details for an editor | [Wiki Home](https://github.com/KotMatrosk1n/KM-Editor/wiki) |

GameBanana pages: [Sword and Shield](https://gamebanana.com/tools/23044), [Scarlet and Violet](https://gamebanana.com/tools/23103), and [Legends Z-A](https://gamebanana.com/tools/23168).

The interface supports English, Spanish, French, German, Russian, Ukrainian, and Simplified Chinese. Installed release builds can check for signed updates from Settings.

## Developing KM Editor

KM Editor uses .NET 10, React, TypeScript, pnpm, Rust, and Tauri 2. Windows builds require:

- .NET SDK `10.0.300`, as defined in [`global.json`](global.json).
- Node.js `24.16.0` or newer and pnpm `11.5.2` or newer, as defined in [`package.json`](package.json).
- The Rust MSVC toolchain with `rustc` `1.77.2` or newer, matching [`Cargo.toml`](apps/desktop/src-tauri/Cargo.toml).
- Visual Studio 2022 Build Tools with Desktop development with C++ and a Windows 10 or 11 SDK.
- Microsoft Edge WebView2 Runtime.

See the [Tauri prerequisites](https://v2.tauri.app/start/prerequisites/) for the Windows toolchain.

```powershell
pnpm install --frozen-lockfile
dotnet restore .\KM.Editor.slnx
pnpm check
pnpm tauri:dev
```

Use `pnpm check` to verify workspace hygiene and compile the desktop and backend projects. Installer and updater release builds also require the signing configuration described in [`docs/releases.md`](docs/releases.md).

## Repository Map

- [`src/`](src/) contains backend workflows, binary formats, API contracts, and the bridge host.
- [`apps/desktop/`](apps/desktop/) contains the React, TypeScript, Vite, and Tauri desktop app.
- [`docs/`](docs/) contains release and repository documentation.

Contributions are welcome. Read [Contributing](CONTRIBUTING.md), the [Code of Conduct](CODE_OF_CONDUCT.md), and the [Security Policy](SECURITY.md) before submitting changes or reports. See [Contributors](CONTRIBUTORS.md) for project credits.

KM Editor is distributed under the [GPL 3.0 only license](LICENSE).
