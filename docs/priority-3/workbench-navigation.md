# Workbench Navigation Model

Status: Priority 3 decision record.

KM Editor uses a project workbench shell. The shell should make project status, available workflows, pending changes, diagnostics, and commands visible without turning each editor into a separate application.

## Shell Regions

The desktop app should use these persistent regions:

- Left navigation rail: top-level sections and domain workflow entry points.
- Top command bar: current project state, global search, command palette entry, and primary project actions.
- Main workspace: the active home, workflow list, workflow detail, change review, or apply result view.
- Inspector area: contextual record details, provenance, validation messages, and field-level help when a workflow needs them.
- Status area: concise background activity, validation state, bridge connection state, and blocked-write state.

The shell should stay dense and work-focused. It should not use a marketing landing page, oversized hero area, or decorative project cards as the default experience.

## Top-Level Sections

The left navigation rail should support these first sections:

- Home: project health, path status, known overrides, recent activity, and next actions.
- Workflows: searchable list of supported domain workflows.
- Changes: pending edit-session changes, change-plan preview, validation, apply, and apply results.
- Diagnostics: project, workflow, bridge, validation, and write diagnostics.
- Settings: project-local preferences and future app settings.

The current shell sections `Project Health`, `Workflows`, and `Changes` can remain the initial implementation names while P4 builds the first vertical slice. `Diagnostics` and `Settings` can be added when their surfaces have real content.

## Navigation Behavior

Opening the app without a project should show the project gate in the main workspace. The gate should offer open, create, and recent-project actions without hiding path requirements or safety rules.

After a project opens, Home becomes the default route. Home should explain whether the project is usable, partially configured, loading, or blocked from editing.

Workflow navigation should be stable:

- The Workflows section lists supported workflows and their availability.
- Selecting a workflow opens a workflow detail route inside the main workspace.
- A workflow can show a table/list view, selected-record inspector, diagnostics, and workflow actions without leaving the workbench shell.
- Workflow detail routes should preserve filters, selection, and scroll position while the project stays open.

The Changes section should be the single place where users review pending changes, create a change plan, validate the plan, apply output, and inspect write results.

The Diagnostics section should aggregate messages from project validation, workflow validation, bridge errors, parsing failures, and apply results. Workflow-level diagnostics can also appear inline near the affected view.

## Search And Commands

Global search belongs in the top command bar and should become active only when there is a searchable project index or command index.

Search should be able to return:

- Project commands.
- Workflows.
- Domain records.
- Files.
- Diagnostics.
- Pending changes.

The command palette should share the same command registry as visible buttons and menus. Commands should expose labels, keyboard shortcuts when available, disabled reasons, and required project/edit-session state.

## Project State In The Shell

The top command bar should always communicate project state:

- No project open.
- Project opening or refreshing.
- Project open and healthy.
- Project open with warnings.
- Project open but blocked from writes.
- Bridge or backend unavailable.

Primary project actions belong in the top command bar or project gate:

- Open project.
- Create project.
- Refresh project.
- Configure paths.
- Close project.

Destructive or write-producing actions should not live as always-visible primary buttons in the shell. They belong in the Changes flow after preview and validation.

## Workflow Availability

Workflows should show one of these availability states:

- Available: project paths and data needed by the workflow are valid.
- Disabled: the workflow is not usable yet, with a short reason.
- Read-only: data can be inspected but not safely written.
- Loading: the workflow is being indexed or loaded.
- Error: the workflow failed to load and has diagnostics.

Disabled workflow entries should remain visible when they are part of the supported product model, because they help users understand what path or project state is missing.

## Future Items Editor Fit

The future Items editor should be a workflow detail route inside the Workflows section, not a separate top-level app mode.

Its expected shell placement is:

- Left navigation: `Workflows` selected, with `Items` selected in the workflow list.
- Main workspace: searchable and filterable item table or list.
- Inspector area: selected item details, provenance, validation, and editable fields.
- Changes section: pending item edits, generated file targets, validation, apply results, and rollback metadata.
- Diagnostics: item data load errors, field validation errors, and output write diagnostics.

Priority 3 only defines this placement. It does not build the Items editor.

## Priority 4 Implication

The first vertical slice should implement only the shell pieces needed to prove this model:

- Project gate or open-project state.
- Home with project health.
- Workflows list with Items as the first planned workflow entry.
- Items workflow detail route once Priority 4 begins.
- Changes surface for pending change review, validation, apply, and results.
- Diagnostics presentation tied to project validation and the first workflow.

