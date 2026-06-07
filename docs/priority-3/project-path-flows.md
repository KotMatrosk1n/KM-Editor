# Project And Path Flow Model

Status: Priority 3 decision record.

KM Editor treats a project as the center of the app. Opening or creating a project establishes base data, optional output data, validation state, recent-project history, and the safe write boundary used by every workflow.

## Project Gate

When no project is open, the main workspace should show a project gate with these actions:

- Open project: select an existing KM Editor project definition.
- Create project: start a new project definition by selecting required paths.
- Recent projects: reopen a known project definition.
- Configure paths: available during create flow and when an existing project has missing paths.

The project gate should be compact and direct. It should not become an onboarding page or product landing page.

## Project Definition

A project definition should capture:

- Project name.
- Base RomFS path.
- Base ExeFS path.
- Optional LayeredFS output root.
- Last opened time.
- Last known project health summary.

The project definition should not store private dumps, generated output, or parsed cache data in source control. It should point to local paths and let the backend validate them when the project opens.

## Create Project Flow

The create flow should be a single focused workflow with these steps:

1. Name the project.
2. Select base RomFS.
3. Select base ExeFS.
4. Optionally select output root.
5. Validate paths.
6. Open the project if validation reaches a usable state.

Base RomFS and base ExeFS are required for editable workflows. Output root can be optional during setup, but write-producing actions must be disabled until a writable output root is configured.

The create flow should allow users to proceed to read-only inspection when base paths validate but output root is missing.

## Open Project Flow

Opening a saved project should:

- Load the project definition.
- Validate each configured path.
- Detect game/version support when possible.
- Build or refresh the file graph.
- Enter Home after validation completes.

If validation fails, the project should still open into a recoverable state when safe. The Home view should show blocked state, diagnostics, and path repair actions.

## Recent Projects

Recent projects should show:

- Project name.
- Last opened time.
- Last known health state.
- Short path summary.
- Missing-path or unavailable-location indicator when known.

Selecting a recent project should not silently assume the previous health state is still true. The backend must revalidate paths and file graph state before workflows become editable.

Recent-project entries should support removal from the list without deleting project files or output folders.

## Path Selection Fields

Path controls should be built for repeated use:

- Each path has a visible label, current value, validation status, and browse action.
- Long paths should truncate in the middle while preserving beginning and ending context.
- Each path should expose copy, reveal-in-file-explorer, clear, and revalidate actions when applicable.
- Invalid paths should keep the entered value visible so users can repair it.

Path controls should distinguish:

- Not set.
- Checking.
- Valid.
- Missing.
- Wrong kind, such as file selected where folder is required.
- Unsupported layout.
- Read-only when writability is required.
- Unavailable, such as disconnected drive or moved folder.

## Path Validation Rules

Base RomFS validation should check:

- The path exists.
- The path is a directory.
- The directory shape looks like a Sword/Shield RomFS dump.
- Required files for the first supported workflows are present.
- The path is treated as read-only source data.

Base ExeFS validation should check:

- The path exists.
- The path is a directory.
- The directory shape looks like a Sword/Shield ExeFS dump.
- Required executable or metadata files are present when needed by supported workflows.
- The path is treated as read-only source data.

Output root validation should check:

- The path is set when write-producing workflows need it.
- The path exists or can be created after explicit user confirmation.
- The path is writable.
- The path is not the same as base RomFS or base ExeFS.
- Existing files are treated as LayeredFS overrides, not disposable output.

Validation should fail closed for writes when a required path is unknown, unsupported, or unsafe.

## Validation Presentation

Path validation should use both summary and detail:

- Summary state on the project gate and Home view.
- Inline state next to each path.
- Diagnostics for specific missing files, unsupported layouts, or blocked writes.
- Clear repair action when the issue can be fixed by selecting a different path.

Validation messages should be specific and actionable. For example, a missing required file should name the expected file role rather than only saying the project is invalid.

## Recoverable Project States

The shell should support these project states:

- No project open: show project gate.
- Opening: show progress and disable project actions that would conflict.
- Needs paths: project exists but one or more required paths are not set.
- Read-only ready: base paths are valid, output root is missing or unavailable.
- Editable ready: base paths and output root are valid for write-producing workflows.
- Blocked: project has an unsafe configuration or validation error that prevents workflows from loading.
- Backend unavailable: project actions are blocked until the bridge/backend recovers.

Read-only ready is a useful state. It lets users inspect data and diagnostics without implying output writes are available.

## Path Safety Rules

The UI must reinforce these safety rules:

- Never write to base RomFS.
- Never write to base ExeFS.
- Only write through an explicit apply action to the configured output root.
- Never treat existing output files as temporary files.
- Require preview and validation before apply.

If a selected output root appears to overlap a base path, write-producing actions should be blocked until the user selects a safe output root.

## Priority 4 Implication

The first vertical slice should implement the smallest version of this model:

- No-project gate with open/create affordance.
- Path entry or selection state for base RomFS, base ExeFS, and output root.
- Backend-backed validation result shape.
- Home summary that distinguishes no project, read-only ready, editable ready, and blocked.
- Disabled write actions when output root is missing or invalid.

