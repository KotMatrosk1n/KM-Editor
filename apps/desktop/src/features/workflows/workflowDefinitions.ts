/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  ArrowLeftRight,
  BadgeCheck,
  BadgePlus,
  Cable,
  Candy,
  Dna,
  Dumbbell,
  FileSpreadsheet,
  Gift,
  GitMerge,
  HandCoins,
  ListChecks,
  MapPinned,
  MapPin,
  Package,
  PackagePlus,
  Save,
  ScanLine,
  Shield,
  ShieldCheck,
  Shirt,
  Sparkle,
  Sparkles,
  Store,
  Table2,
  Trees,
  UsersRound,
  Waypoints,
  Zap,
  type LucideIcon
} from 'lucide-react';

export const workflowDefinitions: Array<{
  id: string;
  label: string;
  description: string;
  icon: LucideIcon;
}> = [
  {
    id: 'items',
    label: 'Items',
    description: 'Item records, names, and source provenance.',
    icon: Package
  },
  {
    id: 'pokemon',
    label: 'Pokemon',
    description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
    icon: Dna
  },
  {
    id: 'moves',
    label: 'Moves',
    description: 'Move stats, target behavior, secondary effects, flags, and source provenance.',
    icon: Zap
  },
  {
    id: 'text',
    label: 'Text and Dialogue Map',
    description: 'Text entries, dialogue references, and source provenance.',
    icon: ListChecks
  },
  {
    id: 'trainers',
    label: 'Trainers',
    description: 'Trainer parties, classes, battle types, and source provenance.',
    icon: UsersRound
  },
  {
    id: 'giftPokemon',
    label: 'Gift Pokemon',
    description: 'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
    icon: Gift
  },
  {
    id: 'tradePokemon',
    label: 'Trade Pokemon',
    description: 'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
    icon: ArrowLeftRight
  },
  {
    id: 'staticEncounters',
    label: 'Static Encounters',
    description: 'Scripted overworld and story encounter records, IV modes, moves, rules, and source provenance.',
    icon: MapPin
  },
  {
    id: 'shops',
    label: 'Shops',
    description: 'Shop inventories, prices, stock limits, and source provenance.',
    icon: Store
  },
  {
    id: 'encounters',
    label: 'Wild Encounters',
    description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
    icon: Trees
  },
  {
    id: 'raidBattles',
    label: 'Raid Battles',
    description: 'Raid Pokemon slots, star probabilities, ability rolls, guaranteed perfect IVs, and source provenance.',
    icon: Shield
  },
  {
    id: 'raidRewards',
    label: 'Raid Rewards',
    description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
    icon: BadgePlus
  },
  {
    id: 'raidBonusRewards',
    label: 'Raid Bonus Rewards',
    description: 'Raid bonus reward tables, item quantities, den usage, and source provenance.',
    icon: BadgeCheck
  },
  {
    id: 'placement',
    label: 'Placement',
    description: 'Placed objects, map coordinates, script links, and source provenance.',
    icon: MapPinned
  },
  {
    id: 'behavior',
    label: 'Behavior',
    description: 'Symbol encounter behavior profiles, model anchors, collision radii, and source provenance.',
    icon: Activity
  },
  {
    id: 'flagworkSave',
    label: 'Flagwork and Save Inspectors',
    description: 'Game flags, save blocks, inspector metadata, and source provenance.',
    icon: Save
  },
  {
    id: 'bagHook',
    label: 'Bag Hook',
    description:
      'Install this first for Royal Candy or Starting Items. It grants nothing by itself; uninstall removes dependent Royal Candy and Starting Items outputs.',
    icon: Cable
  },
  {
    id: 'royalCandy',
    label: 'Royal Candy Workflows',
    description:
      'Requires Bag Hook, uses only Bag Hook slot 1, and patches reserved Royal Candy ExeFS regions. Use Remove Royal Candy to uninstall safely.',
    icon: Candy
  },
  {
    id: 'startingItems',
    label: 'Starting Items',
    description:
      'Requires Bag Hook and uses only slots 2-20. Clear selected slots and apply to remove Starting Items without touching Royal Candy.',
    icon: PackagePlus
  },
  {
    id: 'npcItemGift',
    label: 'NPC Item Gift',
    description:
      'Advanced RomFS editor for fixed NPC, trainer, story, and DLC item gifts. It stages one NPC at a time and patches only owned AMX cells.',
    icon: HandCoins
  },
  {
    id: 'catchCap',
    label: 'Catch Cap Editor',
    description:
      'Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges is locked at Lv.100 because full badges can catch any level.',
    icon: ShieldCheck
  },
  {
    id: 'ivScreen',
    label: 'IV Screen',
    description:
      'Independent ExeFS editor for raw IV numbers on the Pokemon Summary stats graph. It uses its own reserved hook and cave slots.',
    icon: ScanLine
  },
  {
    id: 'hyperTraining',
    label: 'Hyper Training',
    description:
      'Advanced editor for the Battle Tower Hyper Training NPC minimum level cutoff, matching English dialogue, and picker cutoff checks.',
    icon: Dumbbell
  },
  {
    id: 'shinyRate',
    label: 'Shiny Rate',
    description:
      'Advanced editor for the Sword/Shield shiny reroll count in exefs/main.',
    icon: Sparkle
  },
  {
    id: 'typeChart',
    label: 'Type Chart',
    description:
      'Advanced editor for the Sword/Shield type-effectiveness table in exefs/main.',
    icon: Table2
  },
  {
    id: 'fairyGymBoosts',
    label: 'Fairy Gym Boosts',
    description:
      'Preliminary advanced editor that maps Fairy Gym quiz stat boosts to NPC trainers and answer choices.',
    icon: Sparkles
  },
  {
    id: 'fashionUnlock',
    label: 'Fashion Unlock',
    description:
      'Advanced ExeFS editor that unlocks fashion ownership checks without editing the save file.',
    icon: Shirt
  },
  {
    id: 'gymUniformRemoval',
    label: 'Gym Uniform Removal',
    description:
      'Independent ExeFS editor that keeps gym challenge and gym leader battle scripts from changing the player into the gym uniform.',
    icon: Shirt
  },
  {
    id: 'hyperspaceBypass',
    label: 'Hyperspace Bypass',
    description:
      'Advanced S/V ExeFS editor that lets any Pokemon pass the Hyperspace Hole/Fury Hoopa runtime gate.',
    icon: Sparkle
  },
  {
    id: 'dynamaxAdventures',
    label: 'Dynamax Adventures',
    description:
      'Safe editor for normal route Dynamax Adventures rows with backend guarded species, moves, levels, IVs, and ExeFS mirror support.',
    icon: Waypoints
  },
  {
    id: 'spreadsheetImport',
    label: 'Spreadsheet Import',
    description: 'CSV and TSV import profiles that execute through backend edit sessions.',
    icon: FileSpreadsheet
  },
  {
    id: 'modMerger',
    label: 'Mod Merger',
    description:
      'Merge matching RomFS files from two mod folders, resolve overlapping byte edits, and write merged files to Output Root.',
    icon: GitMerge
  }
];
