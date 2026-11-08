# KM Editor

[![Latest release](https://img.shields.io/github/v/release/KotMatrosk1n/KM-Editor?label=release)](https://github.com/KotMatrosk1n/KM-Editor/releases/latest)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A%20%7C%20PLA-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UKR%20%7C%20ZH-orange)

KM Editor is a Windows desktop toolkit for creating and combining Pokémon mods.

[Download the latest release](https://github.com/KotMatrosk1n/KM-Editor/releases/latest) | [Explore the wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki)

## Editors and Tools

Edit Pokémon, trainer teams, and encounters to build your own adventure. Preview animations and customize supported textures and materials in the [3D Model Editor](https://github.com/KotMatrosk1n/KM-Editor/wiki/3D-Model-Editor), or browse game audio in Sound Studio.

Open Mod Merger from Tools to combine compatible mods and resolve conflicts before exporting. See the [Mod Merger guide](docs/mod-merger.md) for supported games, formats, and when you need original game files.

## Supported Games

Each game guide covers supported versions, editors, and output formats, including LayeredFS and Trinity Mod Manager where available.

- [Pokémon Sword and Shield](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Overview)
- [Pokémon Scarlet and Violet](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Overview)
- [Pokémon Legends Z-A](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview)
- [Pokémon Legends Arceus](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Arceus-Overview)

## Getting Started

1. Run the Windows x64 installer and open your game from the welcome hub.
2. In **Project Setup**, select clean Base RomFS and Base ExeFS folders from your own game dump and choose a separate **Output Root**.
3. Set the compression support folder if your game requires it, then select **Validate Paths**. See the [Project Setup guide](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup) for requirements.
4. In an editor, select **Start Editing** and stage your edits. Open **Changes** to review and apply them.

KM Editor writes mods to the Output Root and leaves your base files intact. See [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow) for a walkthrough and [Output Safety and Recovery](https://github.com/KotMatrosk1n/KM-Editor/wiki/Output-Safety-And-Recovery) for History and checkpoints.

## Help and Community

For setup and troubleshooting, see [Installing and Updating](https://github.com/KotMatrosk1n/KM-Editor/wiki/Installing-and-Updating) and [Error Codes and Diagnostics](https://github.com/KotMatrosk1n/KM-Editor/wiki/Error-Codes-And-Diagnostics).

[Join KM Hub on Discord](https://discord.gg/ess4reb5eB) | [Report an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose)

Find KM Editor on GameBanana for [Sword and Shield](https://gamebanana.com/tools/23044), [Scarlet and Violet](https://gamebanana.com/tools/23103), and [Legends Z-A](https://gamebanana.com/tools/23168).

## Languages and Network Use

The interface is available in English, Spanish, French, German, Russian, Ukrainian, and Simplified Chinese.

Update checks use GitHub, and KM Editor may request missing sprite images from Pokémon Showdown. Project files stay on your computer.

## Contributing

See the [Contributing Guide](CONTRIBUTING.md) for development setup and project checks, the [Code of Conduct](CODE_OF_CONDUCT.md) for community standards, and the [Security Policy](SECURITY.md) for private reports.

## License and Asset Credits

KM Editor is an unofficial fan project. Its source code is distributed under the [GPL 3.0 only license](LICENSE). Pokémon names, trademarks, and artwork belong to their respective owners.

Third party assets retain their own terms. See the [Pokémon Showdown sprite credits](https://pokemonshowdown.com/credits) and the notices for [item icons](apps/desktop/public/item-icons/NOTICE.txt), [party sprites](src/KM.Tools/Resources/PartySprites/NOTICE.txt), [Starmobile artwork](apps/desktop/public/sprites/starmobiles/NOTICE.txt), and the [audio decoder](apps/desktop/public/audio-decoder/NOTICE.txt).
