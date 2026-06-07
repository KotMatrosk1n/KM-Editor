# Technical Spine

Status: Priority 1 decision record.

KM Editor uses a clean monorepo so the desktop UI, backend domain engine, shared contracts, tests, and local tools can evolve independently while still sharing one repository history.

## Repository Shape

The intended project structure is:

```text
apps/
  desktop/
src/
  KM.Api/
  KM.Core/
  KM.Formats/
  KM.SwSh/
  KM.Tools/
tests/
  KM.Core.Tests/
  KM.Formats.Tests/
  KM.SwSh.Tests/
  KM.Integration.Tests/
```

`apps/desktop` is the frontend app shell.

`src/KM.Api` contains shared UI/backend contracts, request and response models, change-plan models, validation results, and bridge-facing DTOs. Contracts should describe editor workflows and project operations rather than exposing raw binary storage classes directly to the UI.

`src/KM.Core` contains project loading, file graph resolution, provenance, edit sessions, transactions, validation primitives, change plans, output writes, write manifests, and rollback foundations.

`src/KM.Formats` contains reusable binary and container format code, including structured readers and writers, text containers, FlatBuffer adapters, archive helpers, executable/script helpers, and low-level validation where the logic is not specific to one Sword/Shield workflow.

`src/KM.SwSh` contains Sword/Shield domain workflows, supported game detection, domain models, item workflows, and later trainers, shops, encounters, text, scripts, and patch workflows.

`src/KM.Tools` contains contributor-facing or internal command-line utilities that are useful to keep in source control. Temporary research output, local dumps, generated files, and private fixtures do not belong there.

## Frontend Shell

The frontend shell belongs in `apps/desktop`.

The UI should be a modern React and TypeScript workbench. It should stay workflow-focused: project health, searchable domain tables, detail inspectors, provenance, preview, validation, apply results, and review changed files.

The UI must not directly bind to generated binary structures or storage objects. It should consume typed contracts from `KM.Api` through the chosen local bridge.

## Backend And Domain Engine

The backend/domain engine is .NET-based and lives under `src/`.

The backend owns file IO, parsing, serialization, validation, transactions, and output writes. UI code can request operations and display results, but it should not decide how base data, LayeredFS overrides, pending edits, and generated output are resolved.

Sword/Shield-specific behavior belongs in `KM.SwSh` unless it is clearly reusable format logic, in which case it belongs in `KM.Formats`.

## UI And Backend Boundary

The UI/backend bridge should use typed local request/response contracts. The preferred direction is local RPC over a desktop-safe transport such as stdio or localhost.

Bridge messages should be stable workflow commands, for example:

- Open or validate a project.
- Build or refresh the file graph.
- Start, inspect, update, validate, commit, or discard an edit session.
- Produce a change plan.
- Apply an approved change plan to the output root.
- Return apply results, written files, validation messages, and rollback metadata.

The bridge should carry structured errors with file, domain, row, field, severity, and expected shape when available.

## Transactions And Edit Sessions

KM Editor should establish transactions before adding many editors.

An edit session represents pending user changes that have not been written to output. A transaction groups related changes, records provenance, supports preview and validation, and can be discarded before output writes.

The initial transaction model should support:

- Pending changes separated from base and LayeredFS data.
- Validation before apply.
- Change plans that list target files before writing.
- Apply results that list written files.
- Write manifests for applied output.
- A rollback foundation for replaced output files.

## Tests

Backend tests belong under `tests/` and should mirror the source projects where useful.

Expected backend test categories:

- Unit tests for project model, provenance, transactions, validation, and change-plan logic.
- Format tests for binary readers, writers, and format-specific validation.
- Sword/Shield workflow tests for domain behavior.
- Integration tests for safe read/edit/preview/validate/apply loops using sanitized fixtures.

Frontend tests should live with the frontend app when the app shell exists. The expected frontend test stack is unit/component coverage plus browser-level workflow tests for key editor flows.

Private dumps, private fixtures, and generated outputs must never be committed. Public fixtures must be sanitized and minimal.

## Generated And Local Files

Source control should ignore generated build output, dependency folders, IDE state, local dumps, LayeredFS output roots, scratch research output, temporary probes, logs, coverage, and private fixtures by default.

Repository files can document the naming convention for local-only folders, but public docs should not include private machine paths or unpublished fixture details.

## Source License Notices

The repository license is GNU GPL v3.

New source files should use SPDX identifiers when the file format supports comments:

```text
SPDX-License-Identifier: GPL-3.0-only
```

Generated files should clearly identify their generator when committed. Prefer not to commit generated source unless it is needed for normal builds or contributor workflows.

## Branch And PR Workflow

Priority 1 work should use separate `KM/...` topic branches for meaningful categories, such as architecture docs, ignore rules, backend scaffolding, frontend scaffolding, API contracts, transaction foundations, and test foundations.

Routine topic PRs can be merged after review of scope and checks. Branches should be deleted locally and remotely after merge unless there is a clear reason to keep them.
