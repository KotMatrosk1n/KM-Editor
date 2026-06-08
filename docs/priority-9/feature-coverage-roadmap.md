# Priority 9 Feature Coverage Roadmap

Priority 9 expands KM Editor into a more complete Sword/Shield editor. The target is a safer, newer workflow built around backend-owned parsing and writing, source provenance, preview/validate/apply, diagnostics, and ergonomic searchable inspectors.

## Deliberate Scan Rule

Every Priority 9 feature slice must start with a source and behavior inventory before implementation. The inventory should record:

- The user-facing editor surface and field groups.
- The backing game data files, structures, schemas, parser, and write path.
- Linked side effects, such as dialogue text updates, placement updates, reward-table references, or ExeFS writes.
- The current KM Editor coverage and the exact missing fields or actions.
- The KM-native workflow model: backend parser/writer, validation, preview, apply target, provenance, diagnostics, and desktop interaction.

Implementation should not begin until the matrix for that feature family is updated. This keeps Priority 9 deliberate instead of relying on memory or screenshots.

## Current KM Coverage

KM Editor already has the project and workflow spine needed for safer feature work:

- Project health, file graph, output-root ownership, read-only base RomFS/ExeFS handling, diagnostics, and installable desktop UX.
- Preview/validate/apply edit sessions for supported workflows.
- Items price editing, text/dialogue editing, trainer basics, wild encounter basics, shops inventory basics, raid rewards basics, placement item basics, spreadsheet import, flagwork/save inspection, ExeFS patch readiness, and Royal Candy workflow guidance.
- Pokemon Data read-only coverage for personal records, evolution records, learnsets, names, search, provenance, and diagnostics.

This coverage is not complete yet. Many editor families still need more fields, linked tables, validations, and bulk operations before KM Editor can be considered a full replacement workflow.

## Feature Surface Matrix

| Feature family | Required editor surface | Current KM status | P9 action |
| --- | --- | --- | --- |
| Pokemon Data | Personal, learnset, evolution, TM/TR/tutor compatibility, and enhancement/bulk actions | Read-only personal/evolution/learnset coverage only | Add editable field groups, learnset/evolution row editing, TM/TR/tutor flags, and previewed bulk transforms |
| Trainers | Trainer class, money, mode, items, AI flags, class ball, party slots, species/form/level/ability/item/nature/gender/shiny/EVs/IVs/Dynamax/Gigantamax/moves, bulk helpers | Basic trainer and party edits only | Expand trainer metadata and party stat editing, including IVs, EVs, nature, gender, shiny, Dynamax, Gigantamax, AI flags, and class ball |
| Wild Encounters | Symbol and hidden encounter tables, species/form/probability/level ranges, map/table context, randomize current map | Basic wild table/slot editing | Expand table context, labels, filters, and batch-safe encounter transforms |
| Raid Battles | Den tables, game version, species/form, star probabilities, level table, ability roll, gender, Gigantamax, flawless IV count, drop/bonus reward links, placement usage labels | Raid rewards only, not battle slots | Add raid battle workflow with linked rewards and placement usage diagnostics |
| Raid Rewards | Drop chances and bonus quantities by star rank, item IDs, reward table IDs, usage labels | Basic reward editing | Split drop rewards and bonus quantities clearly, add usage labels and linked raid-slot provenance |
| Static Encounters | Species/form/level, held item, ability, nature, gender, shiny lock, moves, EVs, IVs, Dynamax/Gigantamax, scenario/reference hashes | Not covered as static workflow | Add static encounter inspector first, then validated editing for stable fields |
| Gifts | Gift/egg flag, species/form/level, ball, held item, ability, nature, gender, shiny lock, IVs and flawless/random sentinels, Dynamax/Gigantamax, OT/memory fields, starter placement side effects | Not covered | Add gift workflow with IV sentinel handling and explicit linked placement-text side effects |
| Trades | Received Pokemon, required Pokemon, level/form/item/ball/ability/nature/gender/shiny lock/relearn moves/IVs, OT/memory fields, dialogue text updates | Not covered | Add trade workflow with linked dialogue preview/update support |
| Rentals | Species/form/level, ball, item, nature, gender, ability, moves, EVs, IVs | Not covered | Add rental Pokemon workflow using shared individual-Pokemon stat controls |
| Dynamax Adventures | Species/form/level, ball, ability roll, Gigantamax state, game version, shiny roll, moves, guaranteed perfect IVs, IV overrides, single-capture flags, story gates, UI message IDs | Not covered | Add Dynamax Adventure workflow with IV sentinel validation and encounter-rule grouping |
| Symbol Behavior | Species/form/model behavior, behavior mode strings, hitbox/grass-shake fields, raw behavior parameters, behavior randomizer | Not covered | Add inspector-first workflow; edit named safe fields before raw parameters |
| Moves | Type/category/power/accuracy/PP/priority, target/timing, secondary effects, stat changes, Max Move power, behavior flags, raw effect fields | Not covered | Add parser-backed Moves workflow with grouped editable fields and flag filters |
| Items | Prices, pouch/inventory metadata, field-use behavior, TM/TR machine data, battle boosts, Pokemon effects, raw flags, trade-evolution item bulk action | Price-focused editing only | Expand Items beyond prices, especially field-use behavior and TM/TR machine metadata |
| Shops | Single-shop and multi-shop inventories, hash labels, rotating/multiple inventories, item-name formatting | Basic inventory editing | Improve single/multi labels, inventory grouping, search, and linked item validation |
| Placement | Area/zone/object labels, field items, hidden items, static spawns, trainer references, nests, flags, randomizable item placements | Item-focused placement editing | Expand labels/references and add targeted editors for static spawns, trainer refs, and nest links |
| Text and Dialogue Map | Common text, story text, syntax helpers, dialogue map, search/filter, randomize text | Covered with KM-native text/dialogue workflows | Keep improving provenance, linked references, and syntax guidance as other workflows need text side effects |
| Shiny Rate | ExeFS shiny rate detection and patching | ExeFS patch readiness only | Add focused shiny-rate patch workflow when anchors are stable |
| Royal Sword Tools | Candy workflow, flagwork, story events, trainer map, save inspector, patch manager, dialogue map | Partially covered by Royal Candy, flagwork/save, ExeFS readiness, dialogue map | Keep these as KM workflow guidance/provenance models |
| Master Dump | Data export for analysis | Not a primary editor workflow | Add export/report features only when they support user-facing workflows |

Code references that are not normal user-facing editor surfaces can inform future research but should not outrank visible, high-value editor workflows.

## Pokemon Data Field Inventory

The Pokemon editor is a high-priority target because users rely on it as a central species-data workspace.

| Tab | Fields/actions to cover in KM |
| --- | --- |
| Personal | Base stats HP/Atk/Def/SpA/SpD/Spe and BST, EV yield, wild held items 50%/5%/1%, abilities 1/2/Hidden, type 1/2, egg group 1/2, EXP group, color, gender ratio, base friendship, base EXP, hatch cycles, catch rate, GO/national ID display, evolution stage, form sprite/index, forms count, height, weight, regional/armor/crown dex indexes, regional variant flag, cannot Dynamax flag, present-in-game flag |
| Learnset | Level-up move rows with add/remove/reorder, level validation, move selectors, TM flags, TR flags, type tutors, special tutors |
| Evolve | Up to nine SWSH evolution rows with method, evolves-into species, argument/form/item/move/type context, and level |
| Enhancements | Randomize personal data, amplify EXP, randomize evolutions, remove trade evolutions, evolve every level, randomize learnsets, expand learnsets, metronome learnsets |

KM should present these as backend-owned field groups with search, filters, diagnostics, and change previews. Bulk actions should become previewed transforms that list every affected species and output file before apply.

## Individual Pokemon Stats And IVs

IV editing is a mandatory P9 capability, but it is not species personal data. It appears on individual Pokemon records across several workflows:

- Trainer parties pack HP/Atk/Def/Spe/SpA/SpD IVs into a single flags value, with shiny and Can Dynamax flags in the high bits.
- Gifts and trades expose per-stat IVs with random/perfect-IV sentinel values.
- Static encounters and rentals expose fixed IVs, EVs, nature, gender, ability, held item, and moves.
- Dynamax Adventures and raid battles expose guaranteed perfect IV counts or IV override sentinels.

KM should implement a shared individual-Pokemon stats model for IVs, EVs, nature, gender, ability, shiny, Dynamax, Gigantamax, and moves, while keeping each workflow's binary layout and sentinel validation in the backend.

## Ordered P9 Implementation Queue

1. Pokemon Data editable parity foundation: personal fields, TM/TR/tutor flags, learnset row edits, evolution row edits, and Pokemon-specific preview/validate/apply.
2. Individual Pokemon stat editing across trainer parties first, then gifts/trades/statics/rentals/Dynamax Adventures, with IV handling treated as a required shared capability.
3. Moves Data workflow with grouped move metadata and battle-behavior flags.
4. Expanded Items workflow beyond prices, including field-use behavior, Pokemon effects, battle boosts, and TM/TR machine metadata.
5. Gift, trade, static, rental, and Dynamax Adventure encounter workflows, including linked text/placement side effects.
6. Raid battle workflow and stronger raid reward/bonus reward linking, usage labels, and placement provenance.
7. Shops and placement expansion for multi-inventory shops, static spawns, trainer refs, nest refs, and richer hash labels.
8. Shiny-rate ExeFS patch workflow and any other focused ExeFS tuning patch with stable anchors.
9. Previewed bulk transforms that replace one-click randomizer/enhancement buttons with auditable change plans.
10. Continued workflow guidance, diagnostics grouping, exports/reports, and desktop polish.

## Slice Checklist

Each P9 branch should keep changes focused and test-backed:

1. Update this matrix for the feature family.
2. Add or extend backend parsers/writers in `KM.Formats`.
3. Add Sword/Shield workflow services in `KM.SwSh`.
4. Expose only stable bridge/API DTOs to the frontend.
5. Keep binary parsing and writing out of the frontend.
6. Add validation before any write path.
7. Apply only to the output root through reviewed change plans.
8. Add provenance and diagnostics for every source file and linked side effect.
9. Add focused unit/integration/frontend tests matching the risk.
10. Update the private handoff after decisions, milestones, PRs, or blockers.
