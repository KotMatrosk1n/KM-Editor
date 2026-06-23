# KM Editor

KM Editor is a desktop editor for Pokemon Sword, Pokemon Shield, Pokemon Scarlet, and Pokemon Violet mod projects.

It works through a safer LayeredFS flow: choose a supported game, validate clean RomFS and ExeFS paths, inspect records with source provenance, stage edits, review the change plan, and apply only after validation. Your base dump stays clean, and the app tells you when something looks off before it writes output.

![KM Editor Project Setup](docs/assets/km-editor-project-setup.png)

## What It Does

KM Editor is a desktop editor for Pokémon Sword, Shield, Scarlet, and Violet modding. It is built to make ROM editing cleaner, safer, and less annoying than jumping between a bunch of separate tools and hoping everything lines up correctly.

Before the editors open, KM Editor checks your selected game, clean base RomFS, base ExeFS, and output folder. This helps prevent edits for the wrong game, mismatched files, and the usual “why did this break” nonsense that happens when a project is pointed at the wrong dump.

Once a project is loaded, the app keeps track of where your data is coming from. It can show whether something came from the clean base files, layered overrides, generated output, or pending edits in your current session. That makes it much easier to understand what you are actually changing before you apply anything.

For Sword and Shield, KM Editor supports Pokémon data, trainers, moves, items, encounters, raids, shops, placement objects, behavior data, and several advanced tools. That includes things like Bag Hook, Royal Candy, Starting Items, Catch Cap, Hyper Training, Type Chart, Fairy Gym Boosts, Fashion Unlock, IV Screen, Gym Uniform Removal, Dynamax Adventures, Hyperspace Bypass, and the 60FPS Patch.

For Scarlet and Violet, KM Editor supports Pokémon data, gift Pokémon, trade Pokémon, trainers, moves, items, placement data, static encounters, wild encounters, shops, Tera Raids, and the type chart. Optional S/V project support is available for editors that need deeper access to Scarlet and Violet data, and the S/V cache system helps repeated editor loads feel a lot less painful.

KM Editor stages your changes before applying them. That means you can review edits, remove mistakes, validate the session, and check the planned output before anything gets written. For higher risk edits, especially ExeFS or hook based workflows, the app uses reviewed change plans instead of blindly throwing files into output and hoping for the best.

The app also includes viewers for game text, flagwork metadata, and save inspection. It has a reproducible Randomizer for Pokémon data, encounters, raid rewards, and type chart output. Game Dump can export supported editor data, and Dump Importer can bring supported Sword and Shield dump files back in through CSV, TSV, or JSON.

KM Editor also supports English, Spanish, French, German, Russian, and Ukrainian UI language selection, app update checks on supported builds, and diagnostics that try to explain what actually happened instead of just handing you a vague error and walking away.

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

- [Wiki Home](https://github.com/KotMatrosk1n/KM-Editor/wiki)
- [Project Setup](https://github.com/KotMatrosk1n/KM-Editor/wiki/Project-Setup)
- [Editing Workflow](https://github.com/KotMatrosk1n/KM-Editor/wiki/Editing-Workflow)
- [Hook Architecture](https://github.com/KotMatrosk1n/KM-Editor/wiki/Hook-Architecture)
- [Language Settings](https://github.com/KotMatrosk1n/KM-Editor/wiki/Language-Settings)
- [Game Dump](https://github.com/KotMatrosk1n/KM-Editor/wiki/Game-Dump)
- [Dump Importer](https://github.com/KotMatrosk1n/KM-Editor/wiki/Dump-Importer)
- [Scarlet and Violet Data Cache](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Data-Cache)
- [Scarlet and Violet Shops](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Shops-Editor)
- [Scarlet and Violet Tera Raids](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Tera-Raids-Editor)
- [Desktop app notes](apps/desktop/README.md)
- [Backend test notes](tests/README.md)

Royal Candy, Starting Items, Catch Cap Editor, Hyper Training, Type Chart, Fairy Gym Boosts, Fashion Unlock, IV Screen, Gym Uniform Removal, Dynamax Adventures, and Hyperspace Bypass are available as Advanced Editors with documented ownership rules. 60FPS Patch, Randomizer, Game Dump, Dump Importer, and Mod Merger are available under Tools, with shareable seeds for Randomizer, reviewed export files for Game Dump, reviewed Sword and Shield imports for Dump Importer, one-sided file copy support for SwSh mod merging, and Scarlet and Violet archive or folder merging support. Scarlet and Violet projects can also use optional project support setup and data cache controls for data editors that need expanded archive access. Scarlet and Violet editor coverage now includes shops, Tera Raids, static encounters, visible placement items, and Fashion Unlock alongside the existing data editors. Rental Pokemon remains hidden as work in progress until its runtime safety work is finished. See the [Hook Architecture wiki page](https://github.com/KotMatrosk1n/KM-Editor/wiki/Hook-Architecture), [60FPS Patch](https://github.com/KotMatrosk1n/KM-Editor/wiki/60FPS-Patch), [Randomizer](https://github.com/KotMatrosk1n/KM-Editor/wiki/Randomizer), [Game Dump](https://github.com/KotMatrosk1n/KM-Editor/wiki/Game-Dump), [Dump Importer](https://github.com/KotMatrosk1n/KM-Editor/wiki/Dump-Importer), [Type Chart](https://github.com/KotMatrosk1n/KM-Editor/wiki/Type-Chart), [Scarlet and Violet Type Chart](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Type-Chart), [Scarlet and Violet Shops](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Shops-Editor), [Scarlet and Violet Tera Raids](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Tera-Raids-Editor), [Scarlet and Violet Static Encounters](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Static-Encounters-Editor), [Fairy Gym Boosts](https://github.com/KotMatrosk1n/KM-Editor/wiki/Fairy-Gym-Boosts), [Fashion Unlock](https://github.com/KotMatrosk1n/KM-Editor/wiki/Fashion-Unlock), [Scarlet and Violet Fashion Unlock](https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Fashion-Unlock), [Gym Uniform Removal](https://github.com/KotMatrosk1n/KM-Editor/wiki/Gym-Uniform-Removal), [Rental Pokemon Editor](https://github.com/KotMatrosk1n/KM-Editor/wiki/Rental-Pokemon-Editor), [Dynamax Adventures](https://github.com/KotMatrosk1n/KM-Editor/wiki/Dynamax-Adventures), and [Hyperspace Bypass](https://github.com/KotMatrosk1n/KM-Editor/wiki/Hyperspace-Bypass).
