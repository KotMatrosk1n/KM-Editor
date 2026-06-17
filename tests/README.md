# Tests

Backend test projects live here and mirror the source structure where useful.

The backend test framework is xUnit.

Run backend tests from the repository root:

```powershell
dotnet test KM.Editor.slnx --no-restore
```

## Test Slices

Use the full suite before releases and broad merges. During normal feature work, prefer the targeted slice commands from the repository root:

```powershell
pnpm test:changed:print
pnpm test:changed
pnpm test:fast
pnpm test:full
```

`test:changed` reads changed tracked and untracked files, ignores private handoff and scratch paths, then runs the smallest useful desktop, backend, and integration checks for those paths.

`test:fast` is the normal confidence loop. It keeps desktop typechecking, desktop bridge and shell smoke coverage, core and format coverage, high risk Sword and Shield workflow coverage, and bridge serialization smoke coverage.

`test:full` keeps the old safety net: desktop Vitest plus the full backend solution tests.

The backend assemblies expose xUnit layer traits:

- `Layer=Unit` for `KM.Core.Tests`.
- `Layer=Format` for `KM.Formats.Tests`.
- `Layer=Workflow` for `KM.SwSh.Tests`.
- `Layer=Integration` for `KM.Integration.Tests`.

Performance baselines are also tagged with `Kind=Slow`.

When adding tests, put the strongest assertion at the cheapest layer that can prove it. Prefer focused workflow, format, bridge, or component tests over adding another broad App or dispatcher regression unless the bug needs the full shell.

Bridge dispatcher tests should prove command routing, serialization, and mapper behavior. Feature rules belong in workflow or format tests unless the bridge layer is the thing that can break them.

Public fixtures must be sanitized and minimal. Private dumps, private fixtures, local output roots, and generated scratch data must stay out of source control.
