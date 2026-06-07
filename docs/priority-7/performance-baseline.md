# Priority 7 Performance Baseline

Priority 7 starts with measurement rather than broad optimization. The first baseline uses sanitized synthetic Sword/Shield-shaped fixtures and exercises the same backend-owned project and workflow services used by the desktop bridge.

## Baseline Coverage

- Project open and file graph building across base RomFS, base ExeFS, and LayeredFS output roots.
- Workflow list creation through `SwShWorkflowService.List`.
- Individual workflow loads for Items, Text, Trainers, Shops, Encounters, Raid Rewards, Placement, Flagwork/Save, ExeFS Patches, Royal Candy, and Spreadsheet Import.
- Repeated opened-project workflow loads that currently reparse shared sources such as item metadata and ExeFS compatibility data.
- Large frontend risk identification from current app shape: workflow arrays are stored and rendered as full repeated lists, so future UI work should measure render cost before adding more rows.

## Current Bottleneck Candidates

- `ProjectWorkspaceService.Open` validates paths and rebuilds the recursive file graph. Public workflow load methods call it for each workflow load, so navigation can pay repeated graph scans.
- Several workflow services parse the same source files independently. Shops and Spreadsheet Import load Items metadata; placement, raid rewards, and items also decode item-name sources.
- ExeFS compatibility analysis now has a backend parsed-data cache spine for shared ExeFS/Royal Candy loads. Future cache work should follow the same conservative file-identity invalidation pattern.
- Text workflow loading decodes every selected-language message table into full records and dialogue references.
- Large desktop tables in `apps/desktop/src/App.tsx` filter and map full workflow arrays without virtualization.

## Benchmark Tests

The baseline lives in `tests/KM.SwSh.Tests/Performance`:

- `FullWorkflowLoadingHasSyntheticPerformanceBaseline` measures project open, workflow summaries, and all P6 workflow loads through the public workflow service path.
- `RepeatedOpenedProjectLoadsExposeSharedParseBaseline` measures opened-project loads that are likely candidates for conservative cache reuse in later Priority 7 branches.

The tests assert fixture shape and use generous timing budgets to catch extreme regressions without treating CI as a precise benchmark machine. Focused runs with detailed console output provide timing and allocation evidence for future optimization branches.

## Initial Synthetic Baseline

One focused local run of `dotnet test tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj --no-restore --filter "FullyQualifiedName~Performance" --logger "console;verbosity=detailed"` produced these approximate measurements:

| Probe | Time | Allocated |
| --- | ---: | ---: |
| Project open on synthetic project | 10-19 ms | 1.69 MiB |
| Workflow list through public service | 14 ms | 1.69 MiB |
| Items load through public service | 13 ms | 3.19 MiB |
| Text load through public service | 36 ms | 15.88 MiB |
| Trainers load through public service | 30 ms | 3.72 MiB |
| Shops load through public service | 13 ms | 3.57 MiB |
| Encounters load through public service | 20 ms | 3.05 MiB |
| Raid Rewards load through public service | 18 ms | 2.83 MiB |
| Placement load through public service | 46 ms | 3.70 MiB |
| Flagwork/Save load through public service | 16 ms | 2.00 MiB |
| ExeFS Patches load through public service | 338 ms | 192.16 MiB |
| Royal Candy load through public service | 313 ms | 192.47 MiB |
| Spreadsheet Import load through public service | 8 ms | 3.19 MiB |
| Repeated opened-project ExeFS load | 334 ms | 190.46 MiB |
| Repeated opened-project Royal Candy load | 349 ms | 190.84 MiB |

These numbers are environment-sensitive and should be used for direction, not as product guarantees. The first high-impact backend targets are ExeFS parse/scan reuse and conservative shared-source caching for workflows that currently reload Items or ExeFS data.

## Backend Parse Cache Spine

The first backend cache slice adds a shared parsed-data cache used by `SwShWorkflowService`, `SwShExeFsPatchWorkflowService`, and `SwShRoyalCandyWorkflowService`. Cache entries are keyed by source file path, file length, last-write time, and parsed value type, so changed LayeredFS or base ExeFS sources miss the cache and are decoded again.

Focused cache tests cover both reuse and invalidation:

- Loading ExeFS Patches and then Royal Candy through one `SwShWorkflowService` records one cache miss followed by one cache hit.
- Replacing `exefs/main` after a successful load returns the invalid-file diagnostic instead of stale compatibility records.

One focused local run after this cache slice produced these approximate measurements:

| Probe | Time | Allocated |
| --- | ---: | ---: |
| ExeFS Patches load through public service | 299 ms | 192.16 MiB |
| Royal Candy load after shared ExeFS cache | 9 ms | 2.00 MiB |
| Repeated opened-project ExeFS load | 361 ms | 190.46 MiB |
| Repeated opened-project Royal Candy with shared ExeFS service | 12 ms | 0.32 MiB |

## Lazy Workflow Loading

The desktop shell now lazy-loads workflow payloads when a user navigates directly to an unloaded workflow section. Opening or validating a project still requests only health, file graph, and workflow summaries; heavy archive-backed workflow payloads remain behind explicit per-workflow bridge calls.

The UI uses the existing backend bridge contracts for lazy loads, shows a loading panel for the selected workflow section, and surfaces bridge diagnostics outside the Health page when a workflow request fails. A regression test verifies that direct section navigation triggers one backend Items workflow request, keeps the loading state visible while the request is pending, and does not steal focus if the user navigates away before the response completes.

## Next Optimization Targets

1. Continue shared-source cache work for Items metadata, item-name text, and archive readers where the same file feeds multiple workflows.
2. Avoid rebuilding the whole project graph for every workflow navigation action when paths are unchanged.
3. Move expensive indexing, parsing, validation, or preview planning behind explicit async/background boundaries where the current bridge blocks responsiveness.
4. Virtualize or otherwise cap large desktop repeated lists after backend timings show which workflows produce the largest payloads.
