# Desktop App

The KM Editor desktop frontend shell lives here.

## Stack

- React
- TypeScript
- Vite
- TanStack Query
- Zustand
- TanStack Table
- TanStack Virtual
- React Hook Form
- Zod
- React Resizable Panels
- lucide-react
- Tauri 2

## Commands

Run these from the repository root:

```powershell
pnpm install
pnpm dev
pnpm typecheck
pnpm build
pnpm check
pnpm sidecar:publish
pnpm tauri:dev
pnpm tauri:build
```

Use `pnpm check` to run workspace hygiene checks and compile both the desktop and backend projects.

Tauri dev and build commands publish `src/KM.Tools` as a self-contained sidecar before launching or packaging the desktop app. To refresh only the sidecar, run `pnpm sidecar:publish`; the generated executable is staged under `apps/desktop/src-tauri/binaries/` and is intentionally not committed.

Tauri builds on Windows require Visual Studio Build Tools with the Microsoft C++ toolchain and Windows SDK components available to the Rust MSVC target.

When building from a protected or synced workspace, use a local writable Cargo target cache to avoid generated Rust build artifacts inheriting restrictive workspace ACLs:

```powershell
set "CARGO_TARGET_DIR=%LOCALAPPDATA%\Temp\km-editor-tauri-target"
pnpm tauri:build
```

The Tauri package build produces MSI and NSIS installers under the Cargo target `release/bundle/` directory. The default per-user NSIS install includes both `km-editor-desktop.exe` and the bundled `km-tools-bridge.exe` sidecar.

The desktop app should consume typed contracts from `src/KM.Api` through the chosen local bridge rather than binding directly to backend storage or binary model types.
