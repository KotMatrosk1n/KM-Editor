# Priority 9 pkNX Feature Parity Plan

Priority 9 uses pkNX as a feature and behavior reference for useful Sword/Shield editing coverage. KM Editor should not clone pkNX's WinForms model. The target is a safer replacement workflow built around backend-owned parsing and writing, source provenance, preview/validate/apply, diagnostics, and ergonomic searchable inspectors.

## Current KM Coverage

KM Editor already covers these high-value editor surfaces:

- Project health, file graph, LayeredFS output ownership, and read-only base RomFS/ExeFS handling.
- Items with backend edit sessions, preview, validation, apply, provenance, and spreadsheet import.
- Text and dialogue map inspection/editing.
- Trainers, trainer parties, shops, encounters, raid rewards, placement, flagwork/save metadata, ExeFS patch readiness, and Royal Candy workflow guidance.
- Desktop workflow navigation, lazy loading, virtualized large tables, diagnostics, and installable Tauri app scaffolding.

## pkNX Surface Still Worth Covering

The remaining useful pkNX-style feature families are:

- Pokemon personal data, forms, stats, abilities, dex flags, evolutions, and learnsets.
- Moves and move metadata.
- ExeFS-backed tuning patches such as shiny rate and type chart changes.
- Gift Pokemon, trades, rentals, static encounters, symbol behavior, and other structured encounter sources.
- Raid and Dynamax Adventure coverage beyond reward tables.
- Story/event and script-adjacent inspectors where KM can provide provenance and safer workflow guidance.

## Ordered P9 Implementation Plan

1. Add Pokemon Data as a backend-owned read-only workflow with personal stats, forms, type labels, abilities, dex presence, evolutions, learnsets, provenance, diagnostics, desktop search, and a detail inspector.
2. Extend Pokemon Data toward safe editing for personal stats and supported scalar fields through edit sessions, validation, change-plan preview, and LayeredFS apply.
3. Add Moves Data as a parser-backed workflow with move names, type/category metadata, power/accuracy/PP, flags, searchable desktop table, and provenance.
4. Add evolution and learnset editing where the file formats are well bounded and can be validated before writing.
5. Add focused ExeFS patch workflows for shiny rate and type chart behavior using the existing patch-manager safety model.
6. Add gift/trade/rental/static encounter inspectors first, then editing only after each table has backend validation and source-level diagnostics.
7. Expand raid/Dynamax Adventure workflows where the data source can be represented as stable records rather than opaque archive dumps.
8. Improve workflow guidance, diagnostics grouping, and desktop packaging polish so users understand what is read-only, editable, staged, validated, and applied.

## First P9 Slice

The first implementation slice is Pokemon Data read-only coverage. It establishes the parser, service, bridge DTO, desktop search/inspector, and test pattern for future Pokemon-family editors without committing to writes before the validation model is ready.
