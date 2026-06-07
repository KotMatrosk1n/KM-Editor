# UX And Project Model

Status: Priority 0 direction.

KM Editor should feel like a focused project workbench for Sword/Shield mod editing. It should be fast to scan, clear about file ownership, and built for repeated editing sessions.

## UX Tone

KM Editor should be a dense, professional editor with clear workflow guidance.

It should avoid marketing-style screens, oversized introductory content, and decorative layouts. The first screen after opening a project should help users understand project health, available workflows, and pending work.

## Project Model

A KM Editor project has these main parts:

- Base RomFS: read-only source data.
- Base ExeFS: read-only source data.
- Output root: LayeredFS target folder for generated or modified files.
- File graph: resolved view of base files, existing output overrides, and supported domains.
- Edit session: pending changes that have not been written.
- Apply result: written files, validation output, and rollback information.

## Opening A Project

Opening a project should establish:

- Where base RomFS data lives.
- Where base ExeFS data lives.
- Whether an output root is configured.
- Whether required folders and expected files are present.
- Whether the output root is writable.
- Whether existing output files may affect loaded workflows.

## Project Health

The home view should summarize:

- Detected game and supported version information where available.
- Base path status.
- Output path status.
- File counts and known overrides.
- Warnings about missing, unsupported, or unexpected files.
- Recent apply results or pending changes when available.

## Editor Surfaces

Editor surfaces should favor:

- Searchable tables for large data sets.
- Detail inspectors for selected records.
- Focused actions for common workflows.
- Preview, Validate, Apply, and Review Changed Files steps for mutating workflows.
- Advanced views only when they help inspect or troubleshoot data.

## Global Search

Global search should grow into a way to find:

- Items.
- Trainers.
- Moves.
- Text.
- Shops.
- Encounters.
- Files.
- Commands.

## Error Handling

Errors should point to the exact file, row, field, or expected format whenever possible.

The editor should explain blocked writes clearly and keep unsupported data inspectable when safe.
