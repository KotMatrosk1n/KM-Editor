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
- Pokemon Data coverage for personal records, editable Personal scalar/flag fields, named Personal reference selectors, TM/TR/tutor compatibility flags, editable level-up learnset rows, editable evolution rows, names, search, provenance, diagnostics, and output-root apply.
- Moves Data coverage for move metadata, scalar/effect/stat/flag editing, search, provenance, diagnostics, and output-root apply.
- Gift Pokemon, Trade Pokemon, Static Encounters, Rental Pokemon, and Dynamax Adventures coverage for scripted individual Pokemon records, named selectors where available, EV/IV controls, sentinel/preset/fixed-IV or guaranteed-perfect-IV validation, search, provenance, diagnostics, and output-root apply.
- Raid Battles coverage for regular nest battle slots, species/form, star probabilities, ability roll, gender, Gigantamax state, guaranteed perfect IV count, linked table hashes, linked drop/bonus reward previews, search, provenance, diagnostics, and output-root apply.

This coverage is not complete yet. Many editor families still need more fields, linked tables, validations, and bulk operations before KM Editor can be considered a full replacement workflow.

## Feature Surface Matrix

| Feature family | Required editor surface | Current KM status | P9 action |
| --- | --- | --- | --- |
| Pokemon Data | Personal, learnset, evolution, TM/TR/tutor compatibility, and enhancement/bulk actions | Parser-backed workflow with searchable species inspection, editable Personal scalar/flag fields, core Personal enum selectors, item/ability/species reference selectors, TM/TR/tutor compatibility flags, level-up learnset row editing, evolution row editing, and evolution method/context selectors through preview/validate/apply | Add previewed bulk transforms |
| Trainers | Trainer class, money, mode, items, AI flags, class ball, party slots, species/form/level/ability/item/nature/gender/shiny/EVs/IVs/Dynamax/Gigantamax/moves, bulk helpers | Trainer class, battle mode, trainer-data items, money, heal/gift values, AI flag mask, guarded class-level ball editing, and party species/form/level/item/move/stat/flag editing are guarded by preview/validate/apply; IVs, EVs, nature, gender, shiny, Dynamax, Gigantamax, and named class/species/item/move selectors are covered | Add stronger AI flag labels and safe bulk helpers |
| Wild Encounters | Symbol and hidden encounter tables, species/form/probability/level ranges, map/table context, randomize current map | Basic wild table/slot editing | Expand table context, labels, filters, and batch-safe encounter transforms |
| Raid Battles | Den tables, game version, species/form, star probabilities, level table, ability roll, gender, Gigantamax, flawless IV count, drop/bonus reward links, placement usage labels | Regular nest battle slots covered with species/form, star probabilities, ability roll, gender, Gigantamax, guaranteed perfect IV count, level/drop/bonus table hash provenance, linked drop/bonus reward preview status, source diagnostics, search, preview/validate/apply | Add placement usage labels, deeper reward navigation, advanced hash editing only when safe, and distribution variants separately |
| Raid Rewards | Drop chances and bonus quantities by star rank, item IDs, reward table IDs, usage labels | Basic reward editing with raid-slot link previews exposed from battle slots | Split drop rewards and bonus quantities clearly, add usage labels and deeper linked raid-slot navigation |
| Static Encounters | Species/form/level, held item, ability, nature, gender, shiny lock, moves, EVs, IVs, Dynamax/Gigantamax, scenario/reference hashes | Backend-owned workflow with searchable records, scenario labels, species/item/move selectors, editable stable fields, EV/IV controls, IV presets/sentinels, provenance, diagnostics, and output-root apply | Add linked placement/script reference diagnostics and safer scenario-specific guidance |
| Gifts | Gift/egg flag, species/form/level, ball, held item, ability, nature, gender, shiny lock, IVs and flawless/random sentinels, Dynamax/Gigantamax, OT/memory fields, starter placement side effects | Backend-owned workflow with searchable records, species/item/move selectors, editable stable fields, signed IV controls, IV presets/sentinels, provenance, diagnostics, and output-root apply | Add OT/memory fields when their structures are stable, plus stronger linked placement-text side-effect guidance |
| Trades | Received Pokemon, required Pokemon, level/form/item/ball/ability/nature/gender/shiny lock/relearn moves/IVs, OT/memory fields, dialogue text updates | Backend-owned workflow with searchable requested/received records, species/item/move selectors, editable stable fields, signed IV controls, IV presets/sentinels, relearn moves, OT/memory fields, provenance, diagnostics, and output-root apply | Add linked dialogue preview/update support and stronger side-effect guidance |
| Rentals | Species/form/level, ball, item, nature, gender, ability, moves, EVs, IVs | Backend-owned workflow with searchable records, species/item/move selectors, fixed IV and EV controls, fixed-IV presets, provenance, diagnostics, and output-root apply | Add linked rental-team usage diagnostics and hash-safe advanced metadata editing when needed |
| Dynamax Adventures | Species/form/level, ball, ability roll, Gigantamax state, game version, shiny roll, moves, guaranteed perfect IVs, IV overrides, single-capture flags, story gates, UI message IDs | Backend-owned workflow with searchable records, species/item/move selectors, ability/version/Gigantamax/shiny labels, move editing, guaranteed perfect IV count editing, signed IV override controls, single-capture/story gate editing, hash provenance, diagnostics, and output-root apply | Add linked UI message/save-flag labels, safer 64-bit hash editing only when modeled, grouped encounter filters, and side-effect guidance |
| Symbol Behavior | Species/form/model behavior, behavior mode strings, hitbox/grass-shake fields, raw behavior parameters, behavior randomizer | Not covered | Add inspector-first workflow; edit named safe fields before raw parameters |
| Moves | Type/category/power/accuracy/PP/priority, target/timing, secondary effects, stat changes, Max Move power, behavior flags, raw effect fields | Parser-backed workflow with labels, grouped inspectors, search, provenance, diagnostics, editable scalar/effect/stat/flag fields, validation, and output-root apply | Add filter presets, safer named selectors for enums/effects, and safe bulk transforms |
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

KM now presents core Personal scalar/flag fields as backend-owned edit-session fields with validation, diagnostics, change previews, output-root writes, named selectors for type, egg group, EXP growth, color, held item, ability, and species-reference fields, grouped TM/TR/type-tutor/Armor-tutor compatibility toggles, level-up learnset row add/edit/remove/reorder actions against `wazaoboe_total.bin`, evolution row add/edit/remove/reorder actions against per-species evolution files, and backend-provided evolution method/context selectors. Future Pokemon Data slices should add previewed transforms that list every affected species and output file before apply.

## Moves Data Field Inventory

The Moves workflow covers battle move metadata as backend-owned records. The first slice loads and labels real move files for inspection; editing should follow as its own preview/validate/apply slice.

| Field group | Fields/actions to cover in KM |
| --- | --- |
| Source files | Move data files under `romfs/bin/pml/waza`, move names from `romfs/bin/message/English/common/wazaname.dat`, move descriptions from `romfs/bin/message/English/common/wazainfo.dat`, and type labels from `romfs/bin/message/English/common/typename.dat` |
| Identity | Move ID, version, enabled/usable state, name, description, source layer, file state, and source file |
| Core stats | Type, quality, category, power, accuracy, PP, priority, critical-hit stage, and Max Move power |
| Targeting and timing | Target mode, hit minimum/maximum, turn minimum/maximum, and raw target value |
| Secondary effects | Inflicted condition, inflict percent/count, flinch chance, effect sequence, recoil, and healing |
| Stat changes | Three stat-change slots with stat, stage delta, and percent |
| Behavior flags | Contact, charge, recharge, Protect interaction, reflectable, snatch, mirror, punch, sound, gravity, defrost, distance triple, heal, substitute bypass, sky battle fail, animate ally, dance, and Metronome eligibility |
| Desktop workflow | Search by ID/name/type/category/effect/stat/flag/source, table scan, grouped inspector, diagnostics for missing or unsupported files, and provenance for base versus output overrides |

The current Moves slices add the parser/writer foundation, read-only workflow, desktop inspector, editable scalar/effect/stat/flag fields through backend edit sessions, numeric and pair validation, reviewed change plans, and output-root writes. Future Moves slices should add filter presets, safer named selectors for enums/effects, and previewed bulk transforms.

## Individual Pokemon Stats And IVs

IV editing is a mandatory P9 capability, but it is not species personal data. It appears on individual Pokemon records across several workflows:

- Trainer parties pack HP/Atk/Def/Spe/SpA/SpD IVs into a single flags value, with shiny and Can Dynamax flags in the high bits.
- Gifts and trades expose per-stat IVs with random/perfect-IV sentinel values.
- Static encounters and rentals expose fixed IVs, EVs, nature, gender, ability, held item, and moves.
- Dynamax Adventures and raid battles expose guaranteed perfect IV counts or IV override sentinels.

KM should implement a shared individual-Pokemon stats model for IVs, EVs, nature, gender, ability, shiny, Dynamax, Gigantamax, and moves, while keeping each workflow's binary layout and sentinel validation in the backend. Trainer parties, gifts, trades, static encounters, rentals, regular raid battle records, and Dynamax Adventures now cover IV-related editing through guarded backend edit sessions.

## Scripted Individual Pokemon Field Inventory

Scripted individual Pokemon workflows are high-value P9 targets because they carry individual Pokemon IV data and visible story/gameplay outcomes.

| Workflow | Backing data | Covered fields | Remaining follow-up |
| --- | --- | --- | --- |
| Gift Pokemon | Script event gift Pokemon records, plus species/item/move lookup text | Species, form, level, held item, ball item, ability, nature, gender, shiny lock, Dynamax level, Gigantamax flag, special move, signed per-stat IVs, IV presets, provenance, diagnostics, and output-root apply | OT/memory fields, richer starter/link side-effect guidance, and any text or placement companion edits once their references are modeled |
| Trade Pokemon | Script event trade Pokemon records, plus species/item/move lookup text | Received species/form/level, requested species/form/nature, held item, ball item, ability, nature, gender, shiny lock, Dynamax level, Gigantamax flag, relearn moves, OT/memory fields, signed per-stat IVs, IV presets, provenance, diagnostics, and output-root apply | Linked dialogue preview/update support, text side-effect guidance, and safe grouped filters for request/received pairs |
| Static Encounters | Script event static encounter records, plus species/item/move lookup text | Species, form, level, held item, ability, nature, gender, shiny lock, encounter scenario, Dynamax/Gigantamax, moves, EVs, signed per-stat IVs, IV presets, provenance, diagnostics, and output-root apply | Scenario-specific guidance, linked placement/script usage labels, and safe grouped filters for story, overworld, and special battle records |
| Rental Pokemon | Script event rental Pokemon records, plus species/item/move lookup text | Species, form, level, held item, ball item, ability, nature, gender, trainer ID, moves, EVs, fixed per-stat IVs, fixed-IV presets, provenance, diagnostics, and output-root apply | Linked rental-team usage diagnostics and hash-safe advanced metadata editing when needed |
| Dynamax Adventures | Adventure encounter Pokemon table, plus species/item/move lookup text | Species, form, level, ball item, ability roll, Gigantamax state, game version, shiny roll, moves, guaranteed perfect IV count, signed IV overrides, single-capture flag, story gate, UI/save hash provenance, diagnostics, and output-root apply | Linked UI message/save-flag labels, hash-safe advanced editing, grouped encounter filters, and side-effect guidance |

## Ordered P9 Implementation Queue

1. Pokemon Data editable parity foundation: personal fields, TM/TR/tutor compatibility flags, core enum/reference selectors, level-up learnset row editing, evolution row editing, and evolution method/context selectors are covered with guarded edit sessions; continue with previewed bulk transforms.
2. Individual Pokemon stat editing across trainer parties first, then gifts/trades/statics/rentals/Adventures, with IV handling treated as a required shared capability. Trainer-party, gift, trade, static encounter, rental, regular raid battle, and Adventure IV/stat editing are covered; continue with raid placement usage labels, deeper linked reward navigation, stronger trainer labels, and safe bulk helpers.
3. Moves Data workflow with grouped move metadata, battle-behavior flags, and guarded edit-session writes.
4. Expanded Items workflow beyond prices, including field-use behavior, Pokemon effects, battle boosts, and TM/TR machine metadata.
5. Gift, trade, static, rental, and Adventure encounter workflows, including linked text/placement side effects. Gift, trade, static encounter, rental, and Adventure editing are covered for stable fields; continue with linked side-effect modeling.
6. Raid battle workflow coverage is in place for regular nest battle slots and linked drop/bonus reward preview status; continue with deeper reward navigation, usage labels, placement provenance, and remaining encounter variants.
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
