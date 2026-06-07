# V1 Scope

Status: Priority 0 direction.

V1 is a Sword/Shield project editor that can open a local base dump and optional LayeredFS output root, inspect project health, edit a small number of high-value domains through safe workflows, preview and validate changes, and write controlled output files.

## Included In V1

- Project setup for base RomFS, base ExeFS, and output root.
- Project health checks for missing folders, unexpected layout, known title/output roots, and writable output.
- A file provenance model for base data, LayeredFS data, pending edits, and generated output.
- One complete read/edit/preview/validate/write workflow.
- Changed-file review before output writes.
- A global search foundation that can grow across domains.
- A domain editor foundation that can support fast tables, detail inspectors, and focused action flows.

## First Proof Domain

Items are the first proof domain.

Items are useful, bounded, searchable, and safer to start with than script or executable patching. They are a good first test for project loading, text lookup, table display, editing, validation, preview, and controlled output.

## First Workflow Ladder

1. Open a project.
2. Validate paths.
3. Build the file graph.
4. Show project health.
5. Load item names and item data.
6. Search items.
7. Inspect item provenance.
8. Edit one safe item field.
9. Review the pending change.
10. Validate the change.
11. Preview file output.
12. Apply to the output root.
13. Show written files.

## Out Of Scope For V1

- Direct edits to original base RomFS or ExeFS dumps.
- Whole-game editor coverage.
- Dump extraction, key management, firmware management, or emulator management.
- Randomizer-first design.
- Reverse-engineering tools as the primary V1 experience.
- Direct UI binding to generated binary or storage models.
