# Mod Merger

Mod Merger is available from the tools navigation before a game project is verified. Add mod folders or ZIP, RAR and 7z archives, choose the game and output folder, then review the merge before exporting.

## Basic and Advanced

**Basic** does not require an original game dump. It compares the supplied mods. Identical data and files present in only one source combine automatically. When sources contain different values for the same field, choose the value to keep. Without the original data, the merger cannot know whether a value was intentionally edited or was simply included unchanged in a mod.

**Advanced** uses the matching original game files to distinguish edits from unchanged values. Independent changes combine automatically. For example, a type change and a moveset change for the same Pokémon can coexist. Differing edits to the same field require a choice. A file missing from the original dump receives Basic comparison and a warning. Compressed inputs may require the compression support folder.

## Files and conflicts

Every input file is included in the inventory. Supported game tables merge by record and field. Supported tables include Pokémon, items, moves, trainers, gifts, trades, raids, rewards, encounters and shops, with additional behavior, placement, fashion and battle parameter formats depending on the game. Game text entries also merge separately. Trainer party slots and move slots can be compared separately. Ordered lists such as learnsets are compared as a unit.

Unknown formats, unsupported table extensions and incompatible record layouts retain their complete bytes. When their contents differ, choose a complete file. The merger does not infer a safe binary edit from arbitrary differing bytes. Textures, audio and models use complete asset choices. Other formats without a preserving structural writer also require a complete file choice. Unknown table extensions are reported explicitly.

Supported GFPAK containers can combine independent member changes. Sword and Shield resident encounter, nest and reward members also support field comparison. Packed Trinity data and its descriptor stay paired. Overlaps with supplied loose paths are inspected before those paths are exposed in the output. Packed Trinity inputs currently require Standalone output. Members without recoverable filenames cannot be converted into named loose files. Conflicting packed containers require a complete package choice. Differences in descriptor metadata can also require a complete descriptor choice.

Use the file list and search to locate conflicts. Select values individually or use one source for the displayed conflicts. Review again after changing a choice. Export remains unavailable while conflicts are unresolved or the review is stale.

## Executable patches

IPS and IPS32 files combine by target address in both modes. Patch records and RLE runs are decoded before comparison, so identical overlapping writes combine without conflict. Differing overlapping writes are grouped into address ranges for review. A choice changes only the conflicting range and retains independent writes from both sources.

Build identifiers in patch filenames group patches for the same executable even when they arrive in different folders. Shortened identifiers are expanded only when they match one supplied full identifier. PCHTXT binary sections compile into IPS32 using their build identifier, enabled state, offset shifts and byte order. Disabled sections remain inactive. Unknown directives, mixed build collections and unsupported patch layout changes block export rather than being ignored.

Complete executable images need Advanced mode and the matching original ExeFS files for region comparison. Build identity, segment geometry and unrelated metadata must remain compatible. If a package combines a replacement executable with an IPS patch for that build, the original image is required and the result is written as one merged executable. Basic cannot determine which bytes of a complete executable were intentionally changed.

## Game detection and output

Title folders, executable metadata and recognizable game paths provide detection evidence. The merger asks for an exact edition when only a game family is identifiable. Loose RomFS files alone do not prove whether a package expects Standalone or Bypass installation. Review the detection details and set a source override when needed. An explicit Trinity source layout treats direct paths as RomFS payloads.

Sword and Shield use Standalone output. Scarlet, Violet and Z-A also offer Trinity Mod Manager and Bypass layouts. Standalone output requires compatible descriptor data when loose replacements override packed files. Basic can use a descriptor supplied by the mods; Advanced can use the original dump. Bypass output requires the compatible bypass to be installed separately and does not install it automatically.

The output folder must be separate from the sources and original dump. Unrelated output files remain in place. Differing files that the merger cannot prove it owns block export. Changing output formats removes only reviewed, unchanged files previously written by this merger.

## Review and recovery

Source content, choices, original comparison data and output state are checked again before export. A changed source or destination requires another review. Exports use the shared transaction, ownership and recovery system.

Open **Output safety and recovery** for the reviewed destination to inspect history, recovery, checkpoints and cleanup. Opening these tools invalidates the previous review. Review again before the next export. The tools remain associated with that destination while you edit the merge draft.

The draft retains the inputs, settings and choices locally. A fresh review is required after reopening the editor. A storage warning appears if the draft cannot be retained.

## Limits

A merge accepts up to 64 sources, 100,000 input files, 1 GiB of expanded input and 256 MiB per file. An export supports up to 2,048 file changes and 1 GiB of output. Original comparison data and conflict previews also have bounded memory limits. Larger packages must be divided into smaller groups. Structural table comparison has additional format limits; complete file review is used when safe field reconstruction is unavailable. Executable patches support up to 1,000,000 expanded write bytes per source patch and 4,000,000 per combined patch. PCHTXT sources are limited to 8 MiB. Executable images are limited to 128 MiB of decoded segments each and 512 MiB across a comparison.
