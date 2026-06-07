# Interaction State Model

Status: Priority 3 decision record.

KM Editor should present diagnostics, errors, commands, disabled states, and pending changes consistently across the workbench. Users should understand what is happening, what is blocked, what changed, and what action is safe to take next.

## Diagnostics Model

Diagnostics are structured messages from project validation, workflow loading, domain validation, bridge calls, change-plan creation, and apply operations.

Every diagnostic should carry as much context as is available:

- Severity: info, warning, or error.
- Message.
- Domain or workflow.
- File path or file role.
- Record, row, or item identity when applicable.
- Field when applicable.
- Expected shape or repair hint when applicable.

Diagnostics should appear in three places:

- Inline near the affected field, row, path, or workflow.
- Aggregated in the active workflow or Home view.
- Aggregated in the Diagnostics section for cross-project review.

Inline diagnostics should be closest to the user action. The Diagnostics section should help users understand the whole project state.

## Error Presentation

Errors should be recoverable whenever possible.

Error views should include:

- What failed.
- Whether data was changed.
- Whether output was written.
- What the user can do next.
- Link or action to related diagnostics when available.

Bridge/backend errors should distinguish:

- Backend unavailable.
- Command failed.
- Command timed out.
- Response contract invalid.
- Validation rejected the requested operation.
- Apply failed before writing.
- Apply failed after writing one or more files.

Write-related errors must be especially explicit about whether any files were written and where the write process stopped.

## Pending Change Presentation

Pending changes belong to the edit session and should be visible before output writes.

Workflow surfaces should mark:

- Changed records.
- Changed fields.
- Records with validation errors.
- Records included in the current change plan.

The Changes section should show:

- Pending change count.
- Grouping by workflow and target file.
- Field-level before/after summaries when available.
- Validation state.
- Change-plan state.
- Apply readiness.
- Revert or discard actions.

Pending changes are not output writes. UI copy and command labels should keep that distinction clear.

## Edit Session States

The edit session should expose these states:

- None: no edit session exists.
- Clean: an edit session exists with no pending changes.
- Dirty: pending changes exist.
- Validating: pending changes or a change plan are being validated.
- Invalid: validation produced blocking errors.
- Ready to plan: pending changes can produce a change plan.
- Plan ready: a change plan exists and can be reviewed.
- Applying: an approved plan is being written.
- Applied: apply completed and produced results.
- Failed: validation, planning, or apply failed.

The UI should prevent ambiguous states such as showing Apply as ready when validation is still running or when the output root is blocked.

## Command And Action Pattern

Commands should be represented by shared metadata, even when they are rendered as buttons, menu items, toolbar actions, keyboard shortcuts, or command palette entries.

Command metadata should include:

- Stable command id.
- Label.
- Description when useful.
- Icon when useful.
- Keyboard shortcut when assigned.
- Required project state.
- Required edit-session state.
- Disabled reason.
- Danger level.
- Confirmation requirement.

Visible actions and command-palette actions should use the same disabled/enabled logic.

## Action Placement

Actions should be placed by risk and scope:

- Project actions live in the project gate, Home, or top command bar.
- Workflow navigation actions live in the Workflows section.
- Workflow-local safe actions live in workflow headers or inspectors.
- Field-level actions live near the field.
- Validate, create change plan, apply, and inspect apply result live in Changes.
- Destructive or write-producing actions require preview/validation context.

Avoid duplicate primary actions in multiple places unless they execute the same command and show the same disabled reason.

## Empty, Loading, Error, And Disabled States

Every major surface should define these states:

- Empty: no data exists yet.
- No results: data exists but filters hide it.
- Loading: data or validation is in progress.
- Refreshing: existing data is visible while background refresh runs.
- Error: the surface failed to load or validate.
- Disabled: the surface is intentionally unavailable until requirements are met.
- Read-only: data can be inspected but not changed or written.

State messages should be short, specific, and action-oriented. Disabled states should include the missing requirement whenever possible.

Loading states should preserve layout dimensions to avoid shifting tables, inspectors, and panels.

## Keyboard-First Expectations

The app should be usable without a mouse for normal workbench navigation.

Priority 4 should establish these foundations:

- Tab order follows visible layout.
- Buttons and inputs have visible focus state.
- Left navigation can be reached and used by keyboard.
- Workflow list can be navigated by keyboard.
- Table rows can be selected by keyboard.
- Inspector fields can be edited and reverted by keyboard.
- Escape closes transient popovers or returns focus to the owning control.
- Enter activates focused primary actions where native semantics support it.

Later command-palette work should add keyboard access to commands without replacing normal visible controls.

## Accessibility Expectations

The frontend should prefer semantic HTML and ARIA only where semantics need help.

Baseline expectations:

- Use real buttons for commands.
- Use labels for form controls.
- Use `aria-current` for active navigation.
- Use `aria-disabled` only when native disabled semantics are not appropriate.
- Do not communicate state by color alone.
- Keep contrast sufficient for warnings, errors, disabled text, and focus rings.
- Announce asynchronous validation or apply results through an accessible status region when appropriate.
- Ensure icon-only controls have accessible names.

Dialogs and popovers should trap focus only when they are modal. Non-modal panels should not trap focus.

## Confirmation And Danger States

Dangerous actions include:

- Discard pending changes.
- Clear or replace project paths.
- Apply a change plan that overwrites existing output files.
- Restore or roll back output files.
- Close a project with pending changes.

Dangerous actions should show what will happen and what data is affected. Apply confirmations should rely on the reviewed change plan rather than a vague yes/no prompt.

## Priority 4 Implication

The first vertical slice should implement:

- Shared diagnostic shape in the UI.
- Project and workflow disabled reasons.
- Pending-change indicators.
- Changes section states through validation, plan review, apply, and result.
- Keyboard focus basics for shell navigation and the first workflow.
- Accessible names and semantic controls for all new actions.

