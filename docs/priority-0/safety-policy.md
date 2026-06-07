# Safety And Data Ownership

Status: Priority 0 direction.

KM Editor should make edits through clear project workflows, not silent filesystem mutations. Users should be able to see what data was read, what changed, and what will be written before output files are touched.

## Core Safety Rule

KM Editor must never write to original base RomFS or ExeFS dumps.

Base dumps are source data. They are read-only inputs for parsing, comparison, validation, and output generation.

## Data Ownership Model

- Base data: original dumped RomFS and ExeFS files. Read-only.
- LayeredFS data: existing files under the configured output root.
- Pending data: edits that exist in the current project session but have not been written.
- Generated data: files KM Editor plans to write after preview and validation.
- Scratch data: temporary parse, cache, or research output that must stay separate from source dumps and source control.

## Write Policy

- Writes go only to the configured output root.
- Every write happens through an explicit apply action.
- Apply actions use a change plan that lists target files before writing.
- Users can cancel after preview and before writing.
- Existing output files must be treated as meaningful LayeredFS overrides, not disposable temporary files.
- If an output target already exists, KM Editor should identify whether it is an existing override, a previous KM Editor output, or an unknown external edit whenever possible.

## Validation Policy

- Validate project paths before opening editable workflows.
- Validate domain data before allowing output writes.
- Validation errors should identify the file, domain, field, and expected shape whenever possible.
- Unknown or unsupported file versions should fail closed for writes.
- Unsupported files can remain inspectable when safe, but they should not be written as edited output until the format is understood.

## Backup And Rollback Policy

- Before overwriting an existing output file, KM Editor should preserve enough information to restore the previous output state.
- V1 should record a write manifest for each apply action.
- V1 should preserve previous output bytes for files it replaces.
- Rollback can start as a focused restore-last-apply workflow before growing into a fuller history system.

## User Trust Policy

- Editors should show source/provenance for loaded data.
- Pending changes should be visible separately from written changes.
- Apply results should show exactly which files were written.
- Dangerous actions should be visually and procedurally distinct from ordinary editing.
