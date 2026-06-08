# KM Editor

KM Editor is a desktop editor for Pokemon Sword and Pokemon Shield projects.

The app keeps game parsing and writing in the backend, shows source provenance and diagnostics in the UI, and routes edits through preview, validation, change review, and apply steps before writing to a LayeredFS output root.

## Repository

- `src/`: backend projects, file formats, workflow services, and bridge host.
- `apps/desktop/`: React/Tauri desktop app.
- `tests/`: backend, format, integration, and desktop-facing contract coverage.

## Requirements

- .NET SDK from `global.json`
- Node.js and pnpm
- Rust/MSVC toolchain for Tauri desktop packaging on Windows

## Common Commands

Run these from the repository root:

```powershell
dotnet test .\KM.Editor.slnx --no-restore
pnpm --filter @km-editor/desktop typecheck
pnpm --filter @km-editor/desktop test:run
pnpm --filter @km-editor/desktop tauri:build
```

Desktop-specific notes live in [apps/desktop/README.md](apps/desktop/README.md). Backend test notes live in [tests/README.md](tests/README.md).
