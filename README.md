# KM Editor

[![Latest release](https://img.shields.io/github/v/release/KotMatrosk1n/KM-Editor?label=release)](https://github.com/KotMatrosk1n/KM-Editor/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UKR%20%7C%20ZH-orange)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)](LICENSE)

KM Editor is a Windows desktop modding toolkit for Pokémon Sword and Shield, Pokémon Scarlet and Violet, and Pokémon Legends Z-A. It provides dedicated game data editors, model and audio tools, and a shared workflow for reviewing changes before writing mod output.

[Download the latest release](https://github.com/KotMatrosk1n/KM-Editor/releases/latest) | [Explore the wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) | [Report an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose)

## Editors and Tools

- Edit Pokémon, trainer teams, moves, items, encounters, and other supported records with names, sprites, search, and controls specific to each game.
- Control Sword and Shield trainer battle permissions in [Trainer Dynamax](https://github.com/KotMatrosk1n/KM-Editor/wiki/Trainer-Dynamax), loss behavior in [Trainer Whiteout](https://github.com/KotMatrosk1n/KM-Editor/wiki/Trainer-Whiteout), and den interaction in [Raid Dens](https://github.com/KotMatrosk1n/KM-Editor/wiki/Raid-Dens). Trainer Dynamax and Trainer Whiteout are Beta editors.
- Edit Team Star bosses in the Scarlet and Violet [Starmobiles editor](https://github.com/KotMatrosk1n/KM-Editor/wiki/Starmobiles-Editor), including supported stats, types, abilities, and moves.
- Inspect models and animations, recolor textures, edit supported materials, and restore original assets in the [3D Model Editor](https://github.com/KotMatrosk1n/KM-Editor/wiki/3D-Model-Editor) for all five games.
- Browse and play discovered game recordings in [Sound Studio](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sound-Studio), with seeking, loops, volume controls, and audio meters.
- Review staged changes and output plans, inspect completed History entries, and manage checkpoints and recovery through the shared output tools.
- Organize projects with recents, pins, bookmarks, saved views, and notes. Workbench adds exploration, comparison, and analysis tools that do not write game data.

Supported fields and output rules differ by game. The [wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) explains each editor's workflow, data model, and limits.

## Getting Started

1. Install the latest Windows release.
2. Choose your game in the welcome hub, then select **Open** for that game.
3. Open **Project Setup** and select clean Base RomFS and Base ExeFS folders.
4. Choose a separate Output Root that does not overlap your source folders.
5. Select **Validate Paths**. Scarlet, Violet and Legends Z-A also require an external support folder to be selected first.
6. Open an editor, save or stage your changes, then review the output plan before applying it.

The welcome hub shows release notes filtered by game and restores saved project paths. Loading game data and preparing caches wait until you select **Validate Paths**.

Scarlet, Violet and Legends Z-A require a valid user selected support folder before editors appear. Beta Editors additionally require their setting to be enabled. See [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup) for the complete path requirements.

KM Editor does not include ROM dumps, RomFS, ExeFS, console keys, or save data. You are responsible for obtaining and using required game data in compliance with applicable law.

Regular users do not need .NET, Node.js, Rust, Git, or a separate backend installation.

## Supported Games

| Game family | Complete guide |
| --- | --- |
| Pokémon Sword and Shield | [Sword and Shield Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Overview) |
| Pokémon Scarlet and Violet | [Scarlet and Violet Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Overview) |
| Pokémon Legends Z-A | [Legends Z-A Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview) |

Data models, supported editors, and output rules differ by game. Depending on the workflow, KM Editor can produce standard LayeredFS output or layouts for supported Trinity Mod Manager setups. Keep a separate Output Root for each game.

## Projects and Output

Normal editor changes are staged before they are applied. Open **Changes** to inspect pending targets and remove anything you do not want. Advanced workflows provide their own review and apply plans when they cannot safely share the normal edit session.

Unstaged drafts and staged changes are separate. Moving to Changes preserves staged work; if a local draft still needs attention, the warning identifies that draft before you discard it. Review checks the combined session against current source and output files, and a failed final review blocks output even when the individual edits passed validation.

Clean Base RomFS and Base ExeFS inputs remain untouched. Normal edits write to a separate Output Root, with output history, checkpoints, and recovery tools. Gameplay Settings can also install its reviewed package into a selected emulator data folder. Existing output stays editable after configuration changes, including changes to support folders. Deleting generated files is supported: newly reviewed edits use surviving output with vanilla fallback and write only their required targets. Deleted saved edits are not replayed from History; current file checks still protect reviewed writes.

## Learn More

| What do you need?              | Start here                                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| Set up a project               | [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup)                              |
| Learn the editing workflow     | [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow)                       |
| Use Beta Gameplay Settings     | [Gameplay Settings](https://github.com/KotMatrosk1n/KM-Editor/wiki/Gameplay-Settings)                     |
| Explore Workbench tools        | [Workbench](https://github.com/KotMatrosk1n/KM-Editor/wiki/Workbench)                                    |
| Inspect output or recover a write | [Output Safety and Recovery](https://github.com/KotMatrosk1n/KM-Editor/wiki/Output-Safety-And-Recovery) |
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

## License and Asset Credits

KM Editor includes Pokémon sprites from Pokémon Showdown's public `gen5` and `ani` directories and may use that service when a bundled sprite is missing. See the [Pokémon Showdown credits](https://pokemonshowdown.com/credits) for contributing artists and upstream sources. These files are excluded from KM Editor's GPL license.

Item artwork uses PokeSprite and PokeAPI assets alongside KM category symbols. See the bundled [item artwork notices](apps/desktop/public/item-icons/NOTICE.txt). Starmobile artwork has its own [attribution and license](apps/desktop/public/sprites/starmobiles/NOTICE.txt), and Sound Studio includes [decoder notices](apps/desktop/public/audio-decoder/NOTICE.txt).

KM Editor is an unofficial fan project and is not affiliated with or endorsed by the games' publishers or developers. Related names, trademarks, and artwork belong to their respective owners.

KM Editor source code is distributed under the [GPL 3.0 only license](LICENSE). Other assets remain subject to their own applicable rights and terms.
