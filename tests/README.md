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

The full command is a time-budgeted gate. It builds backend test projects once, then runs bounded desktop, core/format, Sword/Shield, Scarlet/Violet, Pokemon Legends Z-A, shared bridge, and performance shards. The default limits live in `scripts/test-budgets.json`:

- 270 seconds for the complete local gate.
- 240 seconds for an ordinary shard.
- At most 4 concurrent local workers.

Every command writes `TestResults/test-timings.json`. A command that exceeds its budget is terminated with its descendant processes so timed-out test hosts cannot keep files locked.

CI runs the required shards on separate workers and uploads each timing report. Explicit `Kind=Slow` performance baselines run in local full validation and in scheduled or manually dispatched CI validation, but do not extend the pull-request gate.

Parallel test processes that target the same backend project must build that project once before the parallel group and pass `--no-build` to every child. This prevents concurrent MSBuild writes to the shared `obj` tree.

Run an individual CI-equivalent shard with:

```powershell
pnpm test:shard desktop
pnpm test:shard core-formats
pnpm test:shard swsh-general
pnpm test:shard swsh-hooks-royal-cleanup
pnpm test:shard swsh-hooks-royal-behavior
pnpm test:shard swsh-hooks-other
pnpm test:shard integration-sv
pnpm test:shard integration-za
pnpm test:shard integration-bridge
```

The backend assemblies expose xUnit layer traits:

- `Layer=Unit` for `KM.Core.Tests`.
- `Layer=Format` for `KM.Formats.Tests`.
- `Layer=Workflow` for `KM.SwSh.Tests`.
- `Layer=Integration` for `KM.Integration.Tests`.

Performance baselines are also tagged with `Kind=Slow`.

Backend test assemblies use `tests/xunit.runner.json` to enable collection parallelism with a bounded four-thread limit and report tests that run longer than ten seconds.

When adding tests, put the strongest assertion at the cheapest layer that can prove it. Prefer focused workflow, format, bridge, or component tests over adding another broad App or dispatcher regression unless the bug needs the full shell.

Bridge dispatcher tests should prove command routing, serialization, and mapper behavior. Feature rules belong in workflow or format tests unless the bridge layer is the thing that can break them.

Public fixtures must be sanitized and minimal. Private dumps, private fixtures, local output roots, and generated scratch data must stay out of source control.

Integration tests that exercise S/V or Z-A caching must use a cache rooted inside their temporary project. Do not use the default persistent cache location from tests. Reuse immutable serialized fixture payloads across cases and write only the feature files a test needs.
