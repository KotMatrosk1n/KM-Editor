# Workflow Surface Model

Status: Priority 3 decision record.

KM Editor workflows should feel consistent even when different domains need different data. A workflow can be read-only, editable, blocked, loading, or in error, but it should still use the same workbench structure for discovery, selection, inspection, editing, diagnostics, and pending changes.

## Workflow List

The Workflows section should show supported workflows as a searchable list.

Each workflow entry should include:

- Name.
- Short purpose.
- Availability state.
- Required project inputs.
- Whether the workflow is read-only or editable.
- Pending change count when applicable.
- Diagnostic count when applicable.

The list should support keyboard navigation and preserve the selected workflow while users move between Workflows, Changes, and Diagnostics.

Unavailable workflows should remain visible when they are planned or supported by the current product scope. Their disabled reason should say what project state is missing, not simply that the workflow is unavailable.

## Workflow Detail

A workflow detail view should use this default structure:

- Header: workflow name, availability state, primary safe actions, and diagnostics summary.
- Filter/search row: workflow-local search, filters, sort controls, and view options.
- Primary data surface: table, list, tree, or focused workflow panel.
- Inspector: selected record details, provenance, field validation, and edit controls.
- Footer or status area: row counts, selection state, loading state, and background activity.

Workflow detail views should not replace the global workbench shell. They should live inside the main workspace and use the shared command, diagnostics, and change-review model.

## Table Behavior

Large domain datasets should prefer tables when users need scanning, comparison, sorting, filtering, or bulk selection.

Tables should provide:

- Sticky column headers.
- Stable row height.
- Clear selected row state.
- Keyboard row navigation.
- Sort state that is visible and reversible.
- Column filters when useful.
- Virtualization when row counts can become large.
- Empty and no-results states that explain the current filter or project state.

Tables should not silently commit edits. Inline editing can exist where it is safe, but edits should still enter the edit session as pending changes and remain reviewable before output writes.

## List And Tree Behavior

Lists should be used when records are simpler than a table or when card-like density helps scanning. Trees should be used when hierarchy is the primary concept, such as folders, grouped diagnostics, or nested workflow structures.

Lists and trees should provide:

- Keyboard selection.
- Stable item height or virtualized variable-height handling.
- Selection persistence while details refresh.
- Clear disabled, loading, and error states.
- Visible count and filter state.

Lists should avoid decorative cards. They should be compact, data-rich, and easy to scan.

## Inspector Behavior

The inspector should show the selected record or selected workflow context.

Inspectors should prioritize:

- Record identity.
- Source provenance.
- Editable fields.
- Validation messages.
- Related files.
- Pending change status.
- Focused actions.

When no record is selected, the inspector should show a useful empty state for the current workflow, such as selection guidance, workflow summary, or disabled reason.

Inspector edits should be field-scoped and explicit. If a field has derived values or output implications, the inspector should show that context near the field instead of hiding it in the final change plan only.

## Form Behavior

Forms should use the shared validation model instead of ad hoc component-local rules.

Form fields should provide:

- Label.
- Current value.
- Dirty state when changed.
- Validation state.
- Disabled reason when not editable.
- Revert action for changed fields when applicable.
- Keyboard-accessible controls.

Forms should avoid saving directly to output. Submitting a form should update the pending edit session or request validation; output writes still go through change-plan review and apply.

## Panel Behavior

Panels should be used for functional workspace regions such as inspectors, diagnostics, filters, and change review. Panels should not be nested inside decorative cards.

Resizable panels are appropriate when users need to compare a table with details or diagnostics. Panel sizes should have sensible minimums so controls do not collapse into unusable states.

Panel visibility should be commandable:

- Show or hide inspector.
- Show or hide diagnostics.
- Focus table.
- Focus inspector.
- Focus workflow search.

Priority 4 can implement these as simple state transitions before adding a fuller command palette.

## Selection Model

Workflow surfaces should use one selected record as the default model.

Multi-select can be added when a workflow has real batch operations. Until then, avoid implying batch editing exists.

Selection should persist when:

- A user changes top-level shell sections and returns.
- A filter still includes the selected record.
- A record refreshes without changing identity.

Selection should clear when:

- The project closes.
- The workflow reloads with incompatible data.
- The selected record no longer exists after refresh.

## Local Search And Filtering

Workflow-local search should filter only the active workflow. Global search should search across commands, workflows, records, files, diagnostics, and changes once those indexes exist.

Filters should be visible, removable, and reflected in row counts. A no-results state should identify whether no data exists or whether filters hid all records.

## Editing Model

Editable workflow fields should follow this path:

1. User changes a field or performs a focused action.
2. UI validates the local field shape when possible.
3. Backend validates workflow/domain rules when needed.
4. The change enters the edit session as pending.
5. The workflow surface marks the record and field as changed.
6. The Changes section shows the pending change and later creates the change plan.

Workflow surfaces may provide quick revert for field-level changes, but apply-to-output belongs only in the Changes flow.

## Future Items Editor Fit

The future Items workflow should use the default workflow detail structure:

- Header for Items workflow state and diagnostics.
- Search and filter row for item names, identifiers, categories, and changed state.
- Virtualized table for item records.
- Inspector for selected item fields, provenance, validation, and pending change status.
- No direct output write button inside the item table or inspector.

Priority 3 defines this shape only. Priority 4 may implement the first Items detail route after the user starts Priority 4.

## Priority 4 Implication

The first vertical slice should implement:

- Workflows list with availability states.
- A detail route pattern for the first workflow.
- Table/list shell behavior needed for searchable item records.
- Inspector shell behavior for selected record details.
- Pending-change markers without direct output writes.

