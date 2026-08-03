# KM Editor

[![Latest release](https://img.shields.io/github/v/release/KotMatrosk1n/KM-Editor?label=release)](https://github.com/KotMatrosk1n/KM-Editor/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UK%20%7C%20ZH-orange)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)](LICENSE)

KM Editor is a Windows desktop editor for Pokemon Sword and Shield, Pokemon Scarlet and Violet, and Pokemon Legends Z-A mod projects.

It turns complex game data into a workspace you can search, understand, and review. You get the speed of a specialized editor without giving up control over what KM Editor will write. Spend more time making the mod and less time wondering which file just betrayed you.

[Download the latest release](https://github.com/KotMatrosk1n/KM-Editor/releases/latest) | [Explore the complete wiki](https://github.com/KotMatrosk1n/KM-Editor/wiki) | [Report an issue](https://github.com/KotMatrosk1n/KM-Editor/issues/new/choose)

## Why Modders Use KM Editor

* **Find the record you mean.** Search Pokemon, moves, trainers, encounters, shops, placements, and other supported data by useful names instead of working directly with raw tables.
* **Edit with game context.** Controls, choices, limits, and diagnostics follow the selected game and workflow. KM Editor does not pretend that similar looking data works the same way in every game.
* **See the output before it exists.** Stage changes across editors, inspect the complete plan, and remove anything questionable before files are written.
* **Protect your clean source.** Base RomFS and ExeFS stay read only. Generated files go to a separate Output Root that you control.

When a value, layout, or write target cannot be proven safe, KM Editor blocks the action instead of guessing. That makes it useful for quick edits and for larger projects you expect to revisit months later.

## Start Building

1. Install the latest Windows release.
2. Choose the exact game you are editing.
3. Select clean Base RomFS and Base ExeFS folders, then choose a separate Output Root.
4. Select **Validate Paths**.
5. Open **Workflows**, choose an editor, and stage your changes.
6. Open **Changes**, review every target, and apply the output.

Regular users do not need .NET, Node.js, Rust, Git, or a separate backend installation. KM Editor does not include game files.

## Three Games, Three Dedicated Toolsets

Each game family has its own readers, validation rules, editor models, and output behavior. Work from one game cannot silently cross into another.

| Game family | Complete guide |
| --- | --- |
| Pokemon Sword and Shield | [Sword and Shield overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Sword-and-Shield-Overview) |
| Pokemon Scarlet and Violet | [Scarlet and Violet overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Overview) |
| Pokemon Legends Z-A | [Legends Z-A overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview) |

The wiki is the authoritative feature map. It lists every supported editor, field group, advanced tool, output rule, and known limitation without turning this page into a release history.

## Built For Ongoing Projects

Existing Output Root files layer over the clean base, so KM Editor can continue from the mod you already have. Source labels show where loaded data came from, and project validation reloads every editor when you change the selected game or paths.

Normal edits collect in one reviewable session. Dedicated advanced workflows keep their own plans when several files or executable changes must be handled together. A valid plan can still conflict with another mod when both replace the same file, so KM Editor keeps the targets visible and provides game specific Mod Merger workflows where available.

The interface is available in English, Spanish, French, German, Russian, Ukrainian, and Simplified Chinese. Installed releases can discover signed updates and guide you through installation from Settings.

## Learn More

| Need | Start here |
| --- | --- |
| Set up a clean project | [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup) |
| Understand editing and review | [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow) |
| See every supported feature | [Wiki Home](https://github.com/KotMatrosk1n/KM-Editor/wiki) |
| Install or update KM Editor | [Installing and Updating](https://github.com/KotMatrosk1n/KM-Editor/wiki/Installing-and-Updating) |
| Diagnose a problem | [Error Codes And Diagnostics](https://github.com/KotMatrosk1n/KM-Editor/wiki/Error-Codes-And-Diagnostics) |

GameBanana pages: [Sword and Shield](https://gamebanana.com/tools/23044), [Scarlet and Violet](https://gamebanana.com/tools/23103), and [Legends Z-A](https://gamebanana.com/tools/23168).

## Contributing

Contributions are welcome across the desktop app, backend workflows, binary formats, documentation, and localization. Start with [Contributing](CONTRIBUTING.md), then review the [Code of Conduct](CODE_OF_CONDUCT.md) and [Security Policy](SECURITY.md). Release maintainers can find packaging details in [Release Documentation](docs/releases.md).

KM Editor is distributed under the [GPL 3.0 only license](LICENSE).
