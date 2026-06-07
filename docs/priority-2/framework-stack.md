# Framework And Library Stack

Status: Priority 2 initial decision record.

KM Editor uses a pnpm workspace with the desktop frontend in `apps/desktop`.

## Package Workspace

- Package manager: pnpm.
- Workspace packages: `apps/*`.
- Lockfile: `pnpm-lock.yaml` is committed so contributors install the same dependency graph.
- Dependencies remain outside source control through `node_modules/`, which is ignored.
- Frontend build output remains ignored through the existing `dist/` rule.

## Frontend Shell

- App framework: React with TypeScript.
- Build tool: Vite.
- App package: `@km-editor/desktop`.
- The first shell is a project workbench surface. It does not implement the Items editor.

## Frontend Libraries

- Server and bridge-backed data: TanStack Query.
- Small local UI state: Zustand.
- Tables: TanStack Table.
- Virtualized lists: TanStack Virtual.
- Forms: React Hook Form.
- Runtime validation: Zod with React Hook Form resolvers.
- Layout panes: React Resizable Panels.
- Icons: lucide-react.

## Frontend Tests

- Unit and component tests use Vitest.
- Component rendering tests use Testing Library and jsdom.
- Browser workflow tests use Playwright.

Run browser workflow tests from the repository root:

```powershell
pnpm test:workflow
```

The first Playwright coverage targets the desktop Vite shell. Tauri-window and backend-sidecar workflow coverage should wait until the bridge scaffold exists.

## Backend Tests

- Backend unit and integration tests use xUnit.
- Test discovery through `dotnet test` uses `Microsoft.NET.Test.Sdk` plus the xUnit Visual Studio test adapter.
- Backend test projects are marked as test projects and not packable.

Run backend tests from the repository root:

```powershell
dotnet test KM.Editor.slnx --no-restore
```

## Desktop Wrapper Direction

Tauri 2 is the selected initial desktop wrapper because it keeps the frontend lightweight and can run a local backend sidecar. Electron remains the fallback only if Tauri sidecar or backend integration slows development down.

The desktop package has a Tauri 2 scaffold under `apps/desktop/src-tauri`.

Run the desktop shell from the repository root:

```powershell
pnpm tauri:dev
```

Build the desktop shell from the repository root:

```powershell
pnpm tauri:build
```

On Windows, Tauri/Rust builds require Visual Studio Build Tools with the Microsoft C++ toolchain and Windows SDK components available to the Rust MSVC target.

## Bridge Direction

The UI should call the backend through typed request and response contracts. The preferred transport direction is JSON-RPC-style messages over a desktop-safe channel, starting with stdio for a backend sidecar if Tauri integration is smooth.

Contract validation should use generated or mirrored TypeScript types plus Zod validation at the bridge boundary.

The Tauri shell includes the shell plugin needed for future sidecar launch work. The actual backend sidecar binary, command allowlist, JSON-RPC framing, and contract generation belong in the bridge setup branch.
