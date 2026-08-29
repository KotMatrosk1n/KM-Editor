# KM Editor

[![Latest release](https://img.shields.io/github/v/release/KotMatrosk1n/KM-Editor?label=release)](https://github.com/KotMatrosk1n/KM-Editor/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UKR%20%7C%20ZH-orange)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)](LICENSE)

KM Editor is a Windows desktop modding toolkit for Pokémon Sword and Shield, Pokémon Scarlet and Violet, and Pokémon Legends Z-A. It replaces raw tables and mystery IDs with searchable, game-aware editors that show what will be written before anything reaches your mod.

[Download the latest release](https://github.com/KotMatrosk1n/KM-Editor/releases/latest) | [Explore the wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) | [Report an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose)

## Why Modders Use KM Editor

- Find records through readable names, sprites, selectors, filters, and game-aware labels.
- Edit with controls built for each game's verified data instead of treating every title as the same format.
- Review normal changes in one staged plan, with dedicated previews for advanced workflows that need their own write boundary.
- Keep unfinished work organized through project navigation, record tabs, recents, pins, bookmarks, saved views, notes, and read-only analysis tools.

Beta Gameplay Settings provides reviewed native in-game controls on the exact builds listed below. Native packages are update-specific, preserve compatible KM-managed executable output, and block unverified conflicts. See [Gameplay Settings](https://github.com/KotMatrosk1n/KM-Editor/wiki/Gameplay-Settings) for supported paths, installation, removal, and important limits.

Exact editor and advanced-tool coverage differs by game. The [wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) is the authoritative feature guide for the latest public release.

## Start Building

1. Install the latest Windows release.
2. Open KM Editor and choose the exact game you want to edit.
3. Open **Project Setup** and select clean Base RomFS and Base ExeFS folders.
4. Choose a separate, non-overlapping Output Root for the generated mod.
5. Select **Validate Paths**.
6. Open an editor, save or stage your changes, then review the output plan before applying it.

Optional Scarlet/Violet data support requires an external user-selected dependency folder. Legends Z-A archive-backed editors also require a user-selected support folder. See [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup) for the complete path requirements.

KM Editor does not include ROM dumps, RomFS, ExeFS, console keys, or save data. You are responsible for obtaining and using required game data in compliance with applicable law.

Regular users do not need .NET, Node.js, Rust, Git, or a separate backend installation.

## Three Games, Three Dedicated Toolsets

| Game family                | Beta Gameplay Settings build | Complete guide                                                                                            |
| -------------------------- | ---------------------------- | --------------------------------------------------------------------------------------------------------- |
| Pokémon Sword and Shield   | 1.3.2                        | [Sword and Shield Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Overview)     |
| Pokémon Scarlet and Violet | 4.0.0                        | [Scarlet and Violet Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Overview) |
| Pokémon Legends Z-A        | 2.0.2                        | [Legends Z-A Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview)               |

Data models, supported editors, and output rules differ by game. Depending on the workflow, KM Editor can produce standard LayeredFS output or layouts for supported Trinity Mod Manager setups. Keep a separate Output Root for each game.

## Safety for Ongoing Projects

Normal editor changes are staged before they are applied. Open **Changes** to inspect pending targets and remove anything you do not want. Advanced workflows provide their own review and apply plans when they cannot safely share the normal edit session.

Clean Base RomFS and Base ExeFS inputs remain untouched. KM Editor writes to a separate Output Root, tracks KM-managed output separately from foreign or uncertain files, and stops when ownership, source state, or a write target cannot be verified.

## Learn More

| What do you need?              | Start here                                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| Set up a project               | [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup)                              |
| Learn the editing workflow     | [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow)                       |
| Use Beta Gameplay Settings     | [Gameplay Settings](https://github.com/KotMatrosk1n/KM-Editor/wiki/Gameplay-Settings)                     |
| Explore Workbench tools        | [Workbench](https://github.com/KotMatrosk1n/KM-Editor/wiki/Workbench)                                    |
| Browse every supported feature | [Wiki Home](https://github.com/KotMatrosk1n/KM-Editor/wiki)                                               |
| Install or update the editor   | [Installing and Updating](https://github.com/KotMatrosk1n/KM-Editor/wiki/Installing-and-Updating)         |
| Diagnose a problem             | [Error Codes and Diagnostics](https://github.com/KotMatrosk1n/KM-Editor/wiki/Error-Codes-And-Diagnostics) |

KM Editor is also available on GameBanana for [Sword and Shield](https://gamebanana.com/tools/23044), [Scarlet and Violet](https://gamebanana.com/tools/23103), and [Legends Z-A](https://gamebanana.com/tools/23168).

## Languages and Network Use

The interface is available in English, Spanish, French, German, Russian, Ukrainian, and Simplified Chinese. Installed releases can check GitHub for stable updates, and the Windows setup supports installation, update, repair, and uninstall.

Update checks contact GitHub. If a bundled Pokémon sprite is unavailable, the interface may request a fallback image from Pokémon Showdown. Project files remain local and are not uploaded by either request.

## Contributing

Development requirements, setup commands, interface contracts, and project checks are maintained in the [Contributing Guide](CONTRIBUTING.md). Installer and release packaging have separate [Windows Setup](installer/windows/README.md) and [Release](docs/releases.md) documentation.

Contributors should also read the [Code of Conduct](CODE_OF_CONDUCT.md) and [Security Policy](SECURITY.md).

## License and Third-Party Assets

KM Editor includes Pokémon sprites from Pokémon Showdown's public `gen5` and `ani` directories and may use that service as a missing-sprite fallback. See the [Pokémon Showdown credits](https://pokemonshowdown.com/credits) for contributing artists and upstream sources. These files are excluded from KM Editor's GPL license.

Trainer party cards use item artwork, classic item sprites, and the `helditem.png` fallback from [PKHeX](https://github.com/kwsch/PKHeX) `PKHeX.Drawing.PokeSprite` resources. Those assets are redistributed under PKHeX's GPL-3.0 license.

KM Editor is an unofficial fan-made project and is not affiliated with or endorsed by the games' publishers or developers. Related names, trademarks, and artwork belong to their respective owners.

KM Editor source code is distributed under the [GPL 3.0 only license](LICENSE). Third-party assets remain subject to their own applicable rights and terms.
