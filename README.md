# KM Editor

![Version](https://img.shields.io/badge/version-v2.2.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![Built with](https://img.shields.io/badge/built%20with-.NET%2010%20%2B%20Tauri%202-512BD4)
![Supported Games](https://img.shields.io/badge/supports-SwSh%20%7C%20SV%20%7C%20Z--A-red)
![Localization](https://img.shields.io/badge/localized-EN%20%7C%20ES%20%7C%20FR%20%7C%20DE%20%7C%20RU%20%7C%20UK%20%7C%20ZH-orange)
![License](https://img.shields.io/badge/license-GPL--3.0--only-lightgrey)

KM Editor is a desktop editor for Pokemon Sword, Pokemon Shield, Pokemon Scarlet, Pokemon Violet, and Pokemon Legends Z-A mod projects.

It works through a safer LayeredFS flow: choose a supported game, validate clean RomFS and ExeFS paths, inspect records with source provenance, stage edits, review the change plan, and apply only after validation. Your base dump stays clean, and the app tells you when something looks off before it writes output.

![KM Editor Project Setup](docs/assets/km-editor-project-setup.png)

## What It Does

KM Editor is a desktop editor for Pokémon Sword, Shield, Scarlet, Violet, and Legends Z-A modding. It is built to make ROM editing cleaner, safer, and less annoying than jumping between a bunch of separate tools and hoping everything lines up correctly.

Before the editors open, KM Editor checks your selected game, clean base RomFS, base ExeFS, and output folder. This helps prevent edits for the wrong game, mismatched files, and the usual “why did this break” nonsense that happens when a project is pointed at the wrong dump. Changing the Output Root and validating paths reloads every affected editor against the new project, while Switch and Revert clear current work without making you validate the same project again.

Once a project is loaded, the app keeps track of where your data is coming from. It can show whether something came from the clean base files, layered overrides, generated output, or pending edits in your current session. That makes it much easier to understand what you are actually changing before you apply anything.

For Sword and Shield, KM Editor supports Pokémon data, trainers, moves, items, text editing, wild and static encounters, gift and trade Pokémon, raid battles, raid rewards, shops, placement objects, behavior data, flagwork metadata, save inspection, Game Dump, Dump Importer, Mod Merger, Randomizer, 60FPS Patch, and Profanity Filter. The SwSh advanced editors include Bag Hook, Royal Candy, Starting Items, NPC Item Gift, Catch Cap, Hyper Training, Shiny Rate, Type Chart, Fairy Gym Boosts, Fashion Unlock, IV Screen, Gym Uniform Removal, and Dynamax Adventures.

For Scarlet and Violet, KM Editor supports Pokémon data, gift Pokémon, trade Pokémon, trainers, moves, items, placement data, static encounters, wild encounters, shops, Tera Raids, text editing, Game Dump, Dump Importer, Mod Merger, Fashion Unlock, Hyperspace Bypass, and the type chart. Trainer editing includes projected stats for party slots where the loaded Pokemon data is available. Item editing uses readable item type, pocket, group, battle, and field function labels. Pokemon evolution selectors use real item IDs and only offer items whose current metadata marks them for direct evolution use. Optional S/V project support is available for editors that need deeper access to Scarlet and Violet data, and the S/V cache system helps repeated editor loads feel a lot less painful.

For Pokemon Legends Z-A, KM Editor supports Pokémon data, trainers, moves, items, placement data, wild encounters, static encounters, gift Pokémon, trade Pokémon, shops, text editing, Game Dump, Dump Importer, Mod Merger, and the type chart. Trainer editing includes projected stats and uses localized game data for readable trainer names and classes, including Hyperspace trainers. Placement data and wild encounters are grouped into readable locations, static encounters use clearer scenario labels, and item and shop editing keep technical machines selectable by their real item rows. Pokemon evolution selectors use real item IDs and current evolution item metadata. Z-A projects use their own Trinity cache and their own workflow services so Z-A editing stays separate from Sword and Shield plus Scarlet and Violet behavior.

KM Editor stages your changes before applying them. That means you can review edits, remove mistakes, validate the session, and check the planned output before anything gets written. For higher risk edits, especially ExeFS or hook based workflows, the app uses reviewed change plans instead of blindly throwing files into output and hoping for the best.

For Sword and Shield, Randomizer can produce reproducible Pokémon data, encounter, raid reward, and type chart output. Game Dump can export supported editor data, including Scarlet and Violet plus Legends Z-A text data, and Dump Importer can bring supported Sword and Shield, Scarlet and Violet, and Pokemon Legends Z-A dump files back in through CSV, TSV, or JSON where import profiles are available.

KM Editor also supports English, Spanish, French, German, Russian, Ukrainian, and Simplified Chinese UI language selection, app update checks on supported builds, and diagnostics that try to explain what actually happened instead of just handing you a vague error and walking away.

## Build Requirements

KM Editor is currently built and packaged for Windows.

- .NET SDK `10.0.300`, from [`global.json`](global.json). The repo allows `latestFeature` roll-forward.
- Node.js `24.16.0` or newer, from the root [`package.json`](package.json) engines.
- pnpm `11.5.2` or newer. The repo is pinned to `pnpm@11.5.2`.
- Rust MSVC toolchain with `rustc` `1.77.2` or newer, matching [`apps/desktop/src-tauri/Cargo.toml`](apps/desktop/src-tauri/Cargo.toml).
- Visual Studio 2022 Build Tools with the `Desktop development with C++` workload, including MSVC and a Windows 10/11 SDK.
- Microsoft Edge WebView2 Runtime for the Tauri desktop shell. Windows 10 version 1803 and later, plus Windows 11, normally include it; install the Evergreen Runtime if it is missing.

Tauri's Windows prerequisites are documented here: [Tauri v2 prerequisites](https://v2.tauri.app/start/prerequisites/).

Installer and updater release builds also need Tauri updater signing secrets. See [docs/releases.md](docs/releases.md).

## Repository Layout

- [`src/`](src/): backend projects, binary formats, workflow services, API contracts, and bridge host.
- [`apps/desktop/`](apps/desktop/): React, TypeScript, Vite, and Tauri desktop app.
- [`tests/`](tests/): backend, format, integration, and desktop-facing contract coverage.
- [`docs/`](docs/): release notes, release process, and supporting repo docs.

## Documentation

- GameBanana pages: [Sword and Shield](https://gamebanana.com/tools/23044), [Scarlet and Violet](https://gamebanana.com/tools/23103), and [Legends Z-A](https://gamebanana.com/tools/23168)
- [Wiki Home](https://github.com/KotMatrosk1n/KM-Editor/wiki)
- [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup)
- [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow)
- [Language Settings](https://github.com/KotMatrosk1n/KM-Editor/wiki/Language-Settings)
- [Game Dump](https://github.com/KotMatrosk1n/KM-Editor/wiki/Game-Dump)
- [Dump Importer](https://github.com/KotMatrosk1n/KM-Editor/wiki/Dump-Importer)
- [Hook Architecture](https://github.com/KotMatrosk1n/KM-Editor/wiki/Hook-Architecture)
- [Text Viewer](https://github.com/KotMatrosk1n/KM-Editor/wiki/Text-Viewer)
- [Legends Z-A Overview](https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Overview)
- [Desktop app notes](apps/desktop/README.md)
- [Backend test notes](tests/README.md)
