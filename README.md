# KM Editor

[![Latest release](https://img.shields.io/github/v/release/KotMatrosk1n/KM-Editor?label=release)](https://github.com/KotMatrosk1n/KM-Editor/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UKR%20%7C%20ZH-orange)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)](LICENSE)

KM Editor is a Windows desktop modding toolkit for Pokémon Sword and Shield, Pokémon Scarlet and Violet, and Pokémon Legends Z-A.

It replaces raw table wrestling and mystery ID hunting with searchable editors, readable labels, and workflows that show what will be written before anything reaches your mod.

Modding these games already comes with enough mysteries. Your editor should not be one of them.

[Download the latest release](https://github.com/KotMatrosk1n/KM-Editor/releases/latest) | [Explore the wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) | [Report an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose)

## Less Guessing, More Modding

KM Editor gives supported game data an interface built around how modders actually work.

Depending on the selected game, the editor can work with Pokémon, moves, trainers, items, encounters, raids, gifts, trades, shops, placements, type charts, text, and other game systems. Dedicated tools are also available for randomization, CSV/TSV/JSON imports, mod merging, and supported executable changes.

Useful names, sprites, selectors, filters, and game-aware controls make it easier to find the record you intended to edit without keeping a spreadsheet of internal IDs open on another monitor.

Not every game supports the same features because the games themselves do not work the same way. Each family has its own editor coverage, requirements, and output options. The wiki provides the complete breakdown for the latest public release.

## Your Changes Stay Under Your Control

Normal editor changes are staged before they are applied. You can work across multiple editors, open **Changes**, inspect the pending targets, and remove anything you do not want before files are written. Dedicated advanced workflows expose their own preview and apply plans when they cannot safely share the normal edit session.

Your clean Base RomFS and Base ExeFS remain untouched. Generated files are written to a separate Output Root, and supported existing output can be layered over the clean base when you continue an established project.

Project validation checks that the selected game matches the files you provided. Changing the selected game or project paths clears project-scoped editor state. If KM Editor cannot safely handle a file, layout, or write target, it stops and explains the problem instead of guessing.

## Supported Games

| Game family                     | Complete guide                                                                                            |
| ------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Pokémon Sword and Shield        | [Sword and Shield Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Overview)     |
| Pokémon Scarlet and Violet      | [Scarlet and Violet Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Overview) |
| Pokémon Legends Z-A             | [Legends Z-A Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview)               |

Depending on the game and workflow, KM Editor can produce standard LayeredFS output or layouts intended for supported Trinity Mod Manager setups. Keep a separate Output Root for each game.

The wiki is the best place to check exact editor coverage, input requirements, output methods, and known limitations for the latest published version.

## Getting Started

1. Install the latest Windows release.
2. Open KM Editor and choose the exact game you want to edit.
3. Open **Project Setup** and select clean Base RomFS and Base ExeFS folders.
4. Choose a separate, non-overlapping Output Root for the generated mod.
5. Select **Validate Paths**.
6. Open an editor under **Workflows** and save or stage its changes. Advanced tools may provide their own review flow.
7. For normal editor changes, open **Changes**, review the plan, remove anything you do not want, and apply the output.

Optional Scarlet/Violet data support requires an external user-selected dependency folder. Legends Z-A archive-backed editors also require a user-selected support folder. See [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup) for the complete path requirements.

KM Editor does not include ROM dumps, RomFS, ExeFS, console keys, or save data. You are responsible for obtaining and using required game data in compliance with applicable law.

Regular users do not need .NET, Node.js, Rust, Git, or a separate backend installation.

## Languages, Updates, and Network Use

The interface is available in English, Spanish, French, German, Russian, Ukrainian, and Simplified Chinese.

Installed releases can check GitHub for newer stable versions and guide you through supported updates from **Settings**. The Windows setup handles installation, supported updates, repair, and uninstall while clearing rebuildable editor cache without removing user settings unless the user chooses to remove them during uninstall.

Update checks contact GitHub. If a bundled Pokémon sprite is unavailable, the interface may request a fallback image from Pokémon Showdown. Project files remain local and are not uploaded by either request.

## Guides and Help

| What do you need?               | Start here                                                                                                |
| ------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Set up a project                | [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup)                              |
| Learn the editing workflow      | [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow)                       |
| Browse every supported feature  | [Wiki Home](https://github.com/KotMatrosk1n/KM-Editor/wiki)                                               |
| Install or update the editor    | [Installing and Updating](https://github.com/KotMatrosk1n/KM-Editor/wiki/Installing-and-Updating)         |
| Diagnose a problem              | [Error Codes and Diagnostics](https://github.com/KotMatrosk1n/KM-Editor/wiki/Error-Codes-And-Diagnostics) |

You can also find KM Editor on GameBanana for [Sword and Shield](https://gamebanana.com/tools/23044), [Scarlet and Violet](https://gamebanana.com/tools/23103), and [Legends Z-A](https://gamebanana.com/tools/23168).

## Building from Source

Regular users should install KM Editor from the latest GitHub release. The following setup is only required if you want to work with the source code or create your own build.

Development currently requires:

* Windows 10 or Windows 11 on x64
* Git
* .NET SDK 10.0.300 or a later compatible .NET 10 SDK
* Node.js 24.16.0 or newer
* pnpm 11.5.2 or newer
* Rust 1.88.0 or newer with the MSVC toolchain
* Visual Studio 2022 Build Tools with **Desktop development with C++**
* A Windows 10 or Windows 11 SDK
* Microsoft Edge WebView2 Runtime

Clone the repository, install the locked dependencies, and restore the .NET solution:

```powershell
git clone https://github.com/KotMatrosk1n/KM-Editor.git
Set-Location .\KM-Editor

pnpm install --frozen-lockfile
dotnet restore .\KM.Editor.slnx
```

Start the complete desktop development environment:

```powershell
pnpm tauri:dev
```

Build the unbundled desktop executable:

```powershell
pnpm tauri:build
```

The branded Windows installer is produced separately by the custom setup driver. See the [Windows Setup Documentation](installer/windows/README.md) and [Release Documentation](docs/releases.md) before attempting installer or release packaging.

Before submitting a change, run the project checks:

```powershell
pnpm check
```

The root `package.json` contains the current development and build commands. More information about contributing and verification is available in the [Contributing Guide](CONTRIBUTING.md).

## Contributing

KM Editor is open source, and contributions are welcome.

Before getting started, read the [Contributing Guide](CONTRIBUTING.md), [Code of Conduct](CODE_OF_CONDUCT.md), and [Security Policy](SECURITY.md).

Found a bug or have a feature request? [Open an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose).

## Third-Party Assets and Project Status

KM Editor includes Pokémon sprite files downloaded from Pokémon Showdown's public `gen5` and `ani` sprite directories and may use that service as a missing-sprite fallback. The [Pokémon Showdown credits](https://pokemonshowdown.com/credits) document contributing artists and upstream sources. These files are excluded from KM Editor's GPL license, and this repository does not grant additional rights to them.

KM Editor is an unofficial fan-made project and is not affiliated with or endorsed by the games' publishers or developers. All related names, trademarks, and artwork belong to their respective owners.

KM Editor source code is distributed under the [GPL 3.0 only license](LICENSE). Third-party assets remain subject to their own applicable rights and terms.
