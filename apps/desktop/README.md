# Desktop App

Status: Priority 2 frontend shell.

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
- Vitest and Testing Library
- Playwright
- Tauri 2

## Commands

Run these from the repository root:

```powershell
pnpm install
pnpm dev
pnpm typecheck
pnpm build
pnpm test:run
pnpm test:workflow
pnpm tauri:dev
pnpm tauri:build
```

The workflow test command runs Playwright against the Vite desktop shell and starts the dev server automatically when needed.

Tauri builds on Windows require Visual Studio Build Tools with the Microsoft C++ toolchain and Windows SDK components available to the Rust MSVC target.

The desktop app should consume typed contracts from `src/KM.Api` through the chosen local bridge rather than binding directly to backend storage or binary model types.
