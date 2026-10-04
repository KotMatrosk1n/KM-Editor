# Editable YAML Game Dumps

Game Dump exports editable YAML for Sword, Shield, Scarlet, Violet and Legends: Z-A. YAML is the default for every available category. Existing technical export formats and item price CSV, TSV and JSON imports remain available.

## Export, edit and import

1. Open **Game Dump**, select categories and export with **YAML**.
2. Edit the fields under `values` in a text editor. Keep the header, record `id` and `source` fingerprint. Names on record headings and fields under `read_only` are reference information.
3. Open **Dump Importer**, choose a `.yaml` or `.yml` file and select **Preview Import**.
4. Review the staged changes in **Changes**, then use the normal Review and Apply workflow.

Preview does not write game output. If a file has any invalid entry, none of that file's changes are staged, and the previous pending session is retained. Errors identify the filename, one based line and column, and full field path, such as `records[1].party[2].values.level`.

Large categories are split at record boundaries into files of approximately 4 MiB, for example `trainers/trainers-en-0001.yaml`. Import each edited file separately. A single record is kept together, so the target size is not a strict maximum. The importer accepts files up to 64 MiB and limits nesting and entry counts.

## Format

This abbreviated trainer example illustrates the layout. Use the actual identities and fingerprints from your own export:

```yaml
format: km-editor-dump
version: 1
game: "scarlet"
category: "trainers"
language: "en"
document_id: "scarlet/trainers/en/1"
records:
  - id: "123"
    name: "Trainer name"
    source: "keep the exported fingerprint"
    values:
      money: 1000
    party:
      - id: "1"
        name: "Exported party entry name"
        source: "keep this entry's exported fingerprint"
        values:
          species: "Pikachu"
          level: 25
```

Fields and available names depend on the selected game and category. Named choices accept either an unambiguous name from the export language or the corresponding numeric value. Booleans use `true` and `false`. Text is quoted or uses YAML literal blocks to preserve line breaks and control sequences.

Omitted records and fields mean no change to those source values. Removing a list entry does not delete a game record. This format edits existing entries; adding, deleting or rearranging structural entries must use the relevant editor. `null` is not a clearing operation. For an editable text field, an explicitly quoted empty string is an empty value, subject to that editor's rules.

The importer rejects duplicate fields or record IDs, unknown fields, invalid types, unsupported names or IDs, out of range numbers, modified reference fields, incompatible headers and records changed since export. Related fields are also checked by the owning editor. For example, minimum levels cannot exceed maximum levels, and raid probabilities must satisfy the editor's total rules. Anchors, aliases, custom tags and merged mappings are not supported.

## Category coverage

| Game family | YAML categories |
| --- | --- |
| Sword / Shield | Items, Pokémon, Moves, Text, Trainers, Encounters, Gifts, Trades, Static Encounters, Rental Pokémon, Raid Battles, Raid Rewards, Raid Bonus Rewards, Shops, Placement, Behavior, Held Item Chance, Type Chart |
| Scarlet / Violet | Items, Pokémon, Moves, Text, Trainers, Encounters, Tera Raids with fixed and lottery rewards, Static Encounters, Gifts, Trades, Placement, Shops, Type Chart |
| Legends: Z-A | Items, Pokémon, Moves, Text, Trainers, Encounters, Scripted Bosses, Gifts, Trades, Placement, Shops, Behavior, Type Chart |

Available fields follow the current editor and source data. Unsupported fields stay read only. Pokémon data includes existing learnsets, evolution rows and compatibility. Z-A also includes supported alpha moves and verified alpha size configurations. A shared alpha size configuration affects every form using it; conflicting values for the same configuration in one file are rejected.

Shop prices shown as reference values in Scarlet, Violet and Z-A should be edited in the Items dump. Z-A scripted boss action changes use the existing supported selector and move rules; reference phase information does not become editable through YAML. Type Chart retains its separate session requirement and is staged as one complete table. Apply or discard another pending chart before importing a different chart document.

Sword and Shield's Held Item Chance category contains per Pokemon and form `custom_rates` settings with six normal and boosted percentages, plus the legacy global entry. The `custom_rates` field uses integer `0` or `1`, and each percentage group must total at most 100. Setting `custom_rates` to `0` restores inherited chances for that Pokemon without changing its items. The three item choices belong to the Pokemon category. An independently staged rate setting remains protected from a competing import.

The five Scarlet and Violet Titan partner teams cannot enable Terastallization through YAML. Their supported lead identity edits follow the same base form and scene synchronization rules as Trainers. Titan Swapper's Beta battle replacement settings and Sword and Shield Game Options are not YAML categories.

## Reimports and source changes

The exported `document_id` identifies an import batch. A valid reimport replaces that document's prior batch, including removing its previously imported changes when those fields are returned to their original values or omitted. Other documents and unrelated pending edits remain. As with normal editor staging, a new value for the same editor field replaces its competing pending value. Some editor entries, including learnset rows and the type chart, are stored as a single combined edit.

Keep the document identity unchanged, including its game, category, language and part number. Renaming the file does not change its import identity. Do not use a dump for a different game. If underlying records change after export, export again before editing. The ordinary source checks, output review, apply and history behavior still apply.

The schema 2 `manifest.json` remains the export inventory. Exporting selected categories again retains other categories and removes only stale files owned by a trusted prior inventory. User files outside that ownership are retained.
