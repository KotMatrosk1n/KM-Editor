# Product Direction

Status: approved Priority 0 direction.

KM Editor is a modern Pokemon Sword/Shield editor focused on project-level mod editing.

## Primary Audience

KM Editor is for people maintaining real Sword/Shield LayeredFS mod projects over time.

The editor should make repeated edits easier to understand, preview, validate, and write without forcing users to reason directly about every raw storage file.

## Product Promise

KM Editor helps Sword/Shield modders make confident project-level edits by turning base dump data and LayeredFS output into searchable workflows with preview, validation, provenance, and controlled output.

Short version:

KM Editor is a modern Sword/Shield editor for fast, safe, project-level mod editing.

## Product Principles

- The project is the center, not a single opened file.
- Base files, LayeredFS files, pending edits, and generated output are separate concepts.
- Editors should show what they read, what changed, and what will be written.
- Mutating workflows should support preview, validation, provenance, and controlled output.
- Domain workflows, searchable tables, focused inspectors, and guided actions should be preferred over raw object editing.
- Advanced views can exist when useful, but raw storage objects should not be the main experience.

## Initial Non-Goals

- Do not target every Switch Pokemon game before Sword/Shield is solid.
- Do not make a raw binary/object editor the main product.
- Do not make a randomizer-first tool.
- Do not make a dump extraction, key management, firmware management, or emulator management tool.
- Do not start by recreating every existing editor surface.
