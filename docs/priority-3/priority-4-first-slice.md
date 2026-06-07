# Priority 4 First Vertical Slice

Status: Priority 3 planning record.

Priority 4 should implement the smallest real end-to-end workflow that proves the KM Editor project model, workbench shell, backend bridge, edit session, change plan, validation, and controlled output rules. Priority 3 defines this plan only; it does not implement the slice.

## Goal

Priority 4 should prove that KM Editor can open a Sword/Shield project, validate paths, load the first domain workflow, show searchable data with provenance, record one safe pending edit, preview and validate the generated output, apply it only to the configured output root, and report the written files.

The first domain remains Items.

## Required User Flow

Priority 4 should implement this concrete flow:

1. Open or create a project.
2. Configure base RomFS, base ExeFS, and optional output root.
3. Validate project paths.
4. Build the base/LayeredFS file graph.
5. Show Home with project health and path state.
6. Show Workflows with Items as the first available or planned workflow.
7. Open the Items workflow.
8. Load item names and item data through backend-owned parsing.
9. Search and select item records.
10. Show selected item provenance.
11. Edit one safe item field through an edit session.
12. Show the pending change in the workflow surface and Changes section.
13. Validate the pending change.
14. Create and review a change plan with target files.
15. Apply the approved plan to the output root only.
16. Show apply result with written files and diagnostics.

## Minimum Shell Scope

Priority 4 should implement only the shell needed for the first slice:

- Project gate or equivalent no-project view.
- Home project health view.
- Workflows list.
- Items workflow detail route.
- Changes view with pending changes, validation, change-plan review, apply, and apply result.
- Diagnostics presentation tied to project validation, workflow loading, edit validation, and apply.

Diagnostics and Settings can remain minimal unless the slice needs them. A dedicated Settings section is not required for the first slice.

## Minimum Backend Scope

The backend should own:

- Project definition loading and saving where needed.
- Path validation.
- File graph resolution.
- Sword/Shield support detection where available.
- Items data loading.
- Item provenance resolution.
- Edit-session state.
- Item field validation.
- Change-plan creation.
- Output writing.
- Apply result and write manifest shape.

The frontend should not parse or write binary game files.

## Minimum Bridge Scope

Priority 4 should add or firm up typed bridge commands for:

- Project open/create.
- Project validation.
- File graph refresh.
- Workflow list.
- Items workflow load.
- Edit session start/get/discard.
- Item field update.
- Edit-session validation.
- Change-plan creation.
- Change-plan apply.

Bridge responses should use structured payloads and structured errors that match the Priority 2 bridge direction and Priority 3 diagnostics model.

## Minimum Frontend Scope

The frontend should implement:

- Project state store and bridge-backed queries.
- Route or view state for Home, Workflows, Items detail, Changes, and Diagnostics.
- Path controls and validation state.
- Workflows list with availability and disabled reasons.
- Items table or list with search, selection, and provenance markers.
- Items inspector for the one safe editable field.
- Pending-change markers.
- Changes flow states from dirty edit session through apply result.
- Keyboard focus basics and accessible names for new controls.

The Items table should use the established table and virtualization direction if row counts require it. If a smaller data shape is used for early proof, the implementation should still preserve the table/detail model so it can scale.

## First Safe Item Edit

The first editable item field should be chosen for low risk and clear validation.

The field should:

- Be easy to display and search alongside item identity.
- Have a clear valid range or enum.
- Produce a deterministic output file change.
- Avoid script or executable patching.
- Be reversible in the edit session before apply.

The exact field can be selected during Priority 4 based on backend parser readiness and fixture availability.

## Tests

Priority 4 should add tests across the full path:

- Backend tests for project validation and safe output boundaries.
- Backend tests for Items load/edit/validate/change-plan behavior.
- Integration test for read/edit/preview/validate/apply using sanitized fixtures.
- Frontend component tests for project states, workflow availability, pending changes, and disabled reasons.
- Playwright workflow test for the first project-to-change-review path when fixture setup is available.

Tests must not require private dumps or private fixtures in public source control.

## Out Of Scope For Priority 4

Priority 4 should not include:

- Full Items editor coverage.
- Multiple item edit fields unless needed to prove the workflow.
- Trainers, shops, encounters, text, scripts, ExeFS patching, or other domains.
- Randomizer workflows.
- Dump extraction, key management, firmware management, or emulator management.
- A full settings system.
- A full command palette.
- Private fixture details in public docs or tests.

## Acceptance Criteria

Priority 4 is complete when:

- A user can open or create a project and see validated project health.
- The app distinguishes base data, existing LayeredFS data, pending data, and generated output.
- Items can be loaded through the backend and shown in the workbench.
- One safe item field can be edited as a pending change.
- The pending change can be validated.
- A change plan lists target files before writing.
- Apply writes only to the configured output root.
- Apply result lists written files and diagnostics.
- Tests cover the first vertical slice enough to catch broken project, bridge, edit-session, validation, and apply behavior.

## Priority 3 Boundary

This document completes the planning decision for the first Priority 4 slice. It does not authorize starting Priority 4 or building the Items editor before the user explicitly starts that phase.
