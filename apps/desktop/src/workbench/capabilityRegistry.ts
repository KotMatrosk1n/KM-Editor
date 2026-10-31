/* SPDX-License-Identifier: GPL-3.0-only */

import {
  CloudSun,
  SlidersVertical,
  Activity,
  ArrowLeftRight,
  Award,
  BriefcaseBusiness,
  WifiOff,
  BadgeCheck,
  BadgePlus,
  Ban,
  Box,
  BrainCircuit,
  Cable,
  Candy,
  Camera,
  Cctv,
  ChartNoAxesCombined,
  ClipboardCheck,
  ClipboardList,
  Coffee,
  Coins,
  ContactRound,
  Cookie,
  Dices,
  Disc3,
  Dna,
  Download,
  Dumbbell,
  Expand,
  Flag,
  Flower2,
  FlaskConical,
  Gauge,
  Gem,
  Gift,
  Crown,
  KeyRound,
  GitMerge,
  HandCoins,
  Hammer,
  History,
  ListChecks,
  ListOrdered,
  LayoutDashboard,
  Map as MapIcon,
  MapPin,
  MapPinned,
  Megaphone,
  MessageSquareOff,
  Mountain,
  Music,
  NotebookPen,
  Orbit,
  Package,
  PackageOpen,
  PackagePlus,
  Bird,
  Palette,
  Pill,
  Save,
  ScanLine,
  Settings,
  Shield,
  ShieldCheck,
  Shirt,
  Skull,
  Shuffle,
  SlidersHorizontal,
  Sparkle,
  Sparkles,
  Star,
  Store,
  Swords,
  Table2,
  TextCursorInput,
  TicketCheck,
  TreeDeciduous,
  Trees,
  Radar,
  Tornado,
  Upload,
  UsersRound,
  Waypoints,
  Wrench,
  Zap,
  type LucideIcon
} from 'lucide-react';
import type { ProjectGame } from '../bridge/contracts';
import type { CapabilityKind } from './semanticContracts';
import { workbenchSections, type WorkbenchSection } from './workbenchSections';
import { workbenchSectionLocalization } from './sectionLocalization';

export type CapabilityPresentationMaturityViewModel =
  | 'editable'
  | 'readOnly'
  | 'mixed'
  | 'utility';

export type CapabilityNavigationKindViewModel =
  | 'primary'
  | 'workflow'
  | 'utility'
  | 'hidden'
  | 'internal';

export type WorkbenchCapabilityRegistration = {
  capabilityKinds: readonly CapabilityKind[];
  description: string;
  domain: string;
  games: readonly ProjectGame[];
  icon: LucideIcon;
  id: WorkbenchSection;
  label: string;
  maturity: CapabilityPresentationMaturityViewModel;
  navigationKind: CapabilityNavigationKindViewModel;
  workflowDashboardLabel?: string;
  showInWorkflowDashboard: boolean;
  standalone: boolean;
};

const allGames = ['sword', 'shield', 'scarlet', 'violet', 'za'] as const;
const plaGames = [...allGames, 'pla'] as const;
const swordShieldGames = ['sword', 'shield'] as const;
const scarletVioletGames = ['scarlet', 'violet'] as const;
function workflow(
  registration: Omit<
    WorkbenchCapabilityRegistration,
    'capabilityKinds' | 'domain' | 'maturity' | 'navigationKind' | 'showInWorkflowDashboard' | 'standalone'
  > &
    Partial<
      Pick<
        WorkbenchCapabilityRegistration,
        'capabilityKinds' | 'domain' | 'maturity' | 'navigationKind' | 'showInWorkflowDashboard' | 'standalone'
      >
    >
): WorkbenchCapabilityRegistration {
  return {
    capabilityKinds: ['navigation', 'command'],
    domain: registration.domain ?? `workflow.${toContractSegment(registration.id)}`,
    maturity: 'editable',
    navigationKind: 'workflow',
    showInWorkflowDashboard: true,
    standalone: false,
    ...registration
  };
}

function toContractSegment(section: WorkbenchSection) {
  return section
    .replace(/([a-z0-9])([A-Z])/gu, '$1-$2')
    .toLowerCase();
}

export const workbenchCapabilityRegistry = [
  workflow({ id: 'summaryStats', label: 'IV and EV Screen', games: scarletVioletGames, icon: ChartNoAxesCombined,
    description: 'Show raw IVs and numeric EVs on the Pokemon Summary screen.' }),
  workflow({ id: 'bbqRewards', label: 'BBQ Rewards', games: scarletVioletGames, icon: Coins, description: 'Edit Blueberry Quest BP rewards.' }),
  workflow({ id: 'supportBoard', label: 'Support Board', games: scarletVioletGames, icon: ClipboardList, description: 'Edit Support Board BP prices.' }),
  workflow({ id: 'snacksworth', label: 'Snacksworth', domain: 'workflow.snacksworth', games: scarletVioletGames, icon: Cookie, description: 'Choose solo and group quest eligibility for legendary treats.' }),
  workflow({ id: 'titanSwapper', label: 'Titan Swapper', games: scarletVioletGames, icon: Swords,
    description: 'Choose a separate combat Pokemon for each Titan battle phase.' }),
  workflow({ id: 'starmobiles', label: 'Starmobiles', games: scarletVioletGames,
    icon: Star, description: 'Edit Team Star Starmobile levels and combat stats.' }),
  {
    capabilityKinds: ['navigation', 'command'],
    description: 'Configure, validate, and inspect the active project.',
    domain: 'project',
    games: plaGames,
    icon: Activity,
    id: 'health',
    label: 'Project Setup',
    maturity: 'utility',
    navigationKind: 'primary',
    showInWorkflowDashboard: false,
    standalone: false
  },
  {
    capabilityKinds: ['navigation', 'command'],
    description: 'Open workspace tools, recent targets, and project capabilities.',
    domain: 'workspace.home',
    games: plaGames,
    icon: LayoutDashboard,
    id: 'workbench',
    label: 'Workbench',
    maturity: 'utility',
    navigationKind: 'primary',
    showInWorkflowDashboard: false,
    standalone: false
  },
  {
    capabilityKinds: ['navigation', 'command'],
    description: 'Browse the workflows available for the active project.',
    domain: 'workspace.workflows',
    games: plaGames,
    icon: ListChecks,
    id: 'workflows',
    label: 'Workflows',
    maturity: 'utility',
    navigationKind: 'internal',
    showInWorkflowDashboard: false,
    standalone: false
  },
  workflow({
    description: 'Item records, names, and source provenance.',
    games: plaGames,
    icon: Package,
    id: 'items',
    label: 'Items'
  }),
  workflow({
    description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
    games: plaGames,
    icon: Dna,
    id: 'pokemon',
    label: 'Pokemon'
  }),
  workflow({
    description: 'Plan Regular, Mega, and Hyperspace Pokedex placement coherently.',
    games: ['za'],
    icon: ListOrdered,
    id: 'dexLayout',
    label: 'Dex Layout',
    showInWorkflowDashboard: false
  }),
  workflow({
    description: 'Move stats, target behavior, secondary effects, flags, and source provenance.',
    games: plaGames,
    icon: Zap,
    id: 'moves',
    label: 'Moves'
  }),
  workflow({
    description: 'Trainer parties, classes, battle types, and source provenance.',
    games: plaGames,
    icon: UsersRound,
    id: 'trainers',
    label: 'Trainers'
  }),
  workflow({
    description:
      'Swap exact trainer identities across synchronized Story and Infinity pool mirrors without changing pool sizes.',
    games: ['za'],
    icon: ContactRound,
    id: 'trainerPools',
    label: 'Trainer Pools'
  }),
  workflow({
    description:
      'Edit dress-up items, dress-up groups, hair and makeup catalogs, and their exact shop lineups.',
    games: ['za'],
    icon: Palette,
    id: 'fashionCatalog',
    label: 'Fashion Catalog'
  }),
  workflow({
    description: 'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
    games: plaGames,
    icon: Gift,
    id: 'giftPokemon',
    label: 'Gift Pokemon'
  }),
  workflow({
    description: 'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
    games: allGames,
    icon: ArrowLeftRight,
    id: 'tradePokemon',
    label: 'Trade Pokemon'
  }),
  workflow({
    description: 'Scripted overworld and story encounter records, IV modes, moves, rules, and source provenance.',
    games: ['sword', 'shield', 'scarlet', 'violet', 'pla'],
    icon: MapPin,
    id: 'staticEncounters',
    label: 'Static Encounters'
  }),
  workflow({
    description: 'Rental Pokemon records, fixed IVs, EVs, items, moves, and source provenance.',
    games: swordShieldGames,
    icon: TicketCheck,
    id: 'rentalPokemon',
    label: 'Rental Pokemon'
  }),
  workflow({
    description:
      'Safe editor for normal route Dynamax Adventures rows with backend guarded species, moves, levels, IVs, and ExeFS mirror support.',
    games: swordShieldGames,
    icon: Waypoints,
    id: 'dynamaxAdventures',
    label: 'Dynamax Adventures'
  }),
  workflow({
    description: 'Shop inventories, item metadata, and source provenance.',
    games: plaGames,
    icon: Store,
    id: 'shops',
    label: 'Shops'
  }),
  workflow({ id: 'crafting', label: 'Crafting', games: ['pla'], icon: Hammer,
    description: 'Edit existing recipes, ingredients, and result quantities.' }),
  workflow({ id: 'pokemonDrops', label: 'Pokemon Drops', games: ['pla'], icon: PackageOpen,
    description: 'Edit shared field and Alpha drop items and chances.' }),
  workflow({ id: 'research', label: 'Research', games: ['pla'], icon: NotebookPen,
    description: 'Edit supported research task thresholds, points, and rank requirements.' }),
  workflow({ id: 'outbreaks', label: 'Outbreaks', games: ['pla'], icon: Radar,
    description: 'Edit outbreak encounters, counts, selection weights, and duration choices.' }),
  workflow({ id: 'distortions', label: 'Space Time Distortions', games: ['pla'], icon: Tornado,
    description: 'Edit supported distortion encounters, spawn counts, and item selections.' }),
  workflow({ id: 'weather', label: 'Weather', games: ['pla'], icon: CloudSun,
    description: 'Edit area weather selection weights with preserved story conditions.' }),
  workflow({ id: 'gameplayRules', label: 'Gameplay Rules', games: ['pla'], icon: SlidersVertical,
    description: 'Edit supported capture factors, friendship changes, and shiny bonuses.' }),
  workflow({
    description:
      'Control TM recipe availability and tracking-window material visibility independently.',
    games: scarletVioletGames,
    icon: Disc3,
    id: 'tmMachineControls',
    label: 'TM Machine Controls'
  }),
  workflow({
    description:
      'Edit existing Pokedex distribution cells using coordinates observed in each exact region source.',
    games: scarletVioletGames,
    icon: MapIcon,
    id: 'habitatCoordinates',
    label: 'Habitat Coordinates'
  }),
  workflow({
    description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
    games: plaGames,
    icon: Trees,
    id: 'encounters',
    label: 'Wild Encounters'
  }),
  workflow({
    description: 'Tera raid Pokemon, stars, Tera types, boss settings, rewards, and source provenance.',
    games: scarletVioletGames,
    icon: Gem,
    id: 'teraRaids',
    label: 'Tera Raids'
  }),
  workflow({
    description: 'Raid Pokemon slots, star probabilities, ability rolls, guaranteed perfect IVs, and source provenance.',
    games: swordShieldGames,
    icon: Shield,
    id: 'raidBattles',
    label: 'Raid Battles'
  }),
  workflow({
    description: 'Raid drop reward tables, items, per-star drop chances, and provenance.',
    games: swordShieldGames,
    icon: BadgePlus,
    id: 'raidRewards',
    label: 'Raid Rewards'
  }),
  workflow({
    description: 'Raid bonus reward tables, items, per-star quantities, and provenance.',
    games: swordShieldGames,
    icon: BadgeCheck,
    id: 'raidBonusRewards',
    label: 'Raid Bonus Rewards'
  }),
  workflow({
    description: 'Placed objects, map coordinates, script links, and source provenance.',
    games: allGames,
    icon: MapPinned,
    id: 'placement',
    label: 'Placement'
  }),
  workflow({
    description: 'Game-specific Pokemon behavior settings and source provenance.',
    games: [...swordShieldGames, 'za'],
    icon: BrainCircuit,
    id: 'behavior',
    label: 'Behavior'
  }),
  workflow({
    description: 'Text entries, dialogue references, and source provenance.',
    games: plaGames,
    icon: TextCursorInput,
    id: 'text',
    label: 'Text',
    workflowDashboardLabel: 'Text and Dialogue Map'
  }),
  workflow({
    description: 'Game flags, save blocks, inspector metadata, and source provenance.',
    games: swordShieldGames,
    icon: Save,
    id: 'flagworkSave',
    label: 'Flagwork / Save',
    maturity: 'readOnly',
    workflowDashboardLabel: 'Flagwork and Save Inspectors'
  }),
  workflow({
    description:
      'Install this first for Royal Candy or Starting Items. It grants nothing by itself; uninstall removes dependent Royal Candy and Starting Items outputs.',
    games: swordShieldGames,
    icon: Cable,
    id: 'bagHook',
    label: 'Bag Hook'
  }),
  workflow({
    description:
      'Requires Bag Hook, uses only Bag Hook slot 1, and patches reserved Royal Candy ExeFS regions. Use Remove Royal Candy to uninstall safely.',
    games: swordShieldGames,
    icon: Candy,
    id: 'royalCandy',
    label: 'Royal Candy',
    workflowDashboardLabel: 'Royal Candy Workflows'
  }),
  workflow({
    description:
      'Requires Bag Hook and uses only slots 2-20. Clear selected slots and apply to remove Starting Items without touching Royal Candy.',
    games: swordShieldGames,
    icon: PackagePlus,
    id: 'startingItems',
    label: 'Starting Items'
  }),
  workflow({
    description:
      'Edit the 23 Battle Cafe reward rows with searchable items and exact owner percentage totals.',
    games: swordShieldGames,
    icon: Coffee,
    id: 'battleCafeRewards',
    label: 'Battle Cafe Rewards'
  }),
  workflow({
    description:
      'Advanced RomFS editor for fixed NPC, trainer, story, and DLC item gifts. It stages one NPC at a time and patches only owned AMX cells.',
    games: swordShieldGames,
    icon: HandCoins,
    id: 'npcItemGift',
    label: 'NPC Item Gift'
  }),
  workflow({
    description:
      'Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges is locked at Lv.100 because full badges can catch any level.',
    games: swordShieldGames,
    icon: ShieldCheck,
    id: 'catchCap',
    label: 'Catch Cap',
    workflowDashboardLabel: 'Catch Cap Editor'
  }),
  workflow({
    description: 'Set traded Pokemon obedience level limits for each badge count independently of Catch Cap.',
    games: swordShieldGames, icon: Award, id: 'obedience', label: 'Obedience Level'
  }),
  workflow({
    description:
      'Independent ExeFS editor for raw IV numbers on the Pokemon Summary stats graph. Install and uninstall touch only exact IV Screen-owned bytes.',
    games: swordShieldGames,
    icon: ScanLine,
    id: 'ivScreen',
    label: 'IV Screen'
  }),
  workflow({
    description:
      'Advanced editor for the Battle Tower Hyper Training NPC minimum level cutoff, matching English dialogue, and picker cutoff checks.',
    games: swordShieldGames,
    icon: Dumbbell,
    id: 'hyperTraining',
    label: 'Hyper Training'
  }),
  workflow({
    description: 'Advanced editor for the Sword/Shield shiny reroll count in exefs/main.',
    games: swordShieldGames,
    icon: Sparkle,
    id: 'shinyRate',
    label: 'Shiny Rate'
  }),
  workflow({
    description: 'Advanced editor for the Sword/Shield type-effectiveness table in exefs/main.',
    games: plaGames,
    icon: Table2,
    id: 'typeChart',
    label: 'Type Chart'
  }),
  workflow({
    description:
      'Advanced Z-A editor for both flowers’ HP and Ange’s direct damage to Pokemon and the player.',
    games: ['za'],
    icon: Flower2,
    id: 'angeFight',
    label: 'Ange Fight'
  }),
  workflow({
    description: 'Edit the verified Fairy Gym quiz boost and drop outcomes for every answer.',
    games: swordShieldGames,
    icon: Sparkles,
    id: 'fairyGymBoosts',
    label: 'Fairy Gym Boosts'
  }),
  workflow({
    description: 'Edit Marnie cheering outcomes in the three Wyndon battles.',
    games: swordShieldGames, icon: Megaphone, id: 'marnieBoosts', label: 'Marnie Wyndon Boosts'
  }),
  workflow({
    description: 'Restore missing Pokemon and manage their project assets.',
    games: swordShieldGames, icon: Bird, id: 'restoreMissingPokemon', label: 'Restore Missing Pokemon'
  }),
  workflow({
    description: 'Edit tree items, quantities, encounter tables, and shake risk.',
    games: swordShieldGames, icon: TreeDeciduous, id: 'shakingTrees', label: 'Shaking Trees'
  }),
  workflow({
    description: 'Choose items and normal or boosted chances for each Pokemon and form.',
    games: swordShieldGames, icon: Dices, id: 'heldItemChance', label: 'Held Item Chance'
  }),
  workflow({
    description: 'Choose new game option values and hide individual menu selections.',
    games: swordShieldGames, icon: SlidersHorizontal, id: 'gameOptions', label: 'Game Options'
  }),
  workflow({
    description:
      'Advanced ExeFS editor that unlocks fashion ownership checks without editing the save file.',
    games: ['sword', 'shield', 'scarlet', 'violet'],
    icon: Shirt,
    id: 'fashionUnlock',
    label: 'Fashion Unlock'
  }),
  workflow({
    description:
      'Independent ExeFS editor that keeps gym challenge and gym leader battle scripts from changing the player into the gym uniform.',
    games: swordShieldGames,
    icon: Ban,
    id: 'gymUniformRemoval',
    label: 'Gym Uniform Removal'
  }),
  workflow({
    description:
      'Advanced S/V ExeFS editor that lets any Pokemon pass the Hyperspace Hole/Fury Hoopa runtime gate.',
    games: scarletVioletGames,
    icon: Orbit,
    id: 'hyperspaceBypass',
    label: 'Hyperspace Bypass'
  }),
  workflow({
    description: 'Inspect and stage verified executable patch records.',
    games: swordShieldGames,
    icon: Wrench,
    id: 'exefsPatches',
    label: 'ExeFS Patches',
    navigationKind: 'hidden',
    showInWorkflowDashboard: false
  }),
  workflow({
    description: 'Install, inspect, or restore the verified 60FPS patch.',
    games: swordShieldGames,
    icon: Gauge,
    id: 'fpsPatch',
    label: '60FPS Patch',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Install, inspect, or restore the profanity-filter bypass.',
    games: swordShieldGames,
    icon: MessageSquareOff,
    id: 'profanityFilter',
    label: 'Profanity Filter Bypass',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Disable or restore interaction with raid dens.',
    games: swordShieldGames, icon: Mountain, id: 'raidDens', label: 'Raid Dens',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Disable or restore Poke Jobs at Pokemon Center terminals.',
    games: swordShieldGames, icon: BriefcaseBusiness, id: 'pokeJobs', label: 'Disable Poke Jobs',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Hide Y Comm and block its button while keeping Mystery Gift available.',
    games: swordShieldGames, icon: WifiOff, id: 'yComm', label: 'Disable Y Comm',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Create Pokemon gifts received with custom codes.',
    games: swordShieldGames, icon: KeyRound, id: 'mysteryGift', label: 'Mystery Gift',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Create a random Pokemon reward for each badge count, claimed once per save.',
    games: swordShieldGames, icon: Crown, id: 'royalGift', label: 'Royal Gift',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Review and repair migrated trainer AI flags.',
    games: swordShieldGames, icon: Flag, id: 'fixAiFlags', label: 'Fix AI Flags',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Control trainer whiteout behavior.',
    games: swordShieldGames, icon: Skull, id: 'trainerWhiteout', label: 'Trainer Whiteout',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Control Dynamax permissions in trainer battles.',
    games: swordShieldGames, icon: Expand, id: 'trainerDynamax', label: 'Trainer Dynamax',
    showInWorkflowDashboard: false, standalone: true
  }),
  workflow({
    description: 'Build deterministic reviewed randomizer output.',
    games: swordShieldGames,
    icon: Shuffle,
    id: 'randomizer',
    label: 'Randomizer',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Create a structured game dump for the selected game.',
    games: allGames,
    icon: Download,
    id: 'gameDump',
    label: 'Game Dump',
    maturity: 'utility',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description:
      "Install build-specific controls in the game's own settings menu.",
    games: allGames,
    icon: FlaskConical,
    id: 'gameplaySettings',
    label: 'Gameplay Settings',
    maturity: 'mixed',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Unlock the exploration camera with an in game Options setting.',
    games: swordShieldGames,
    icon: Camera,
    id: 'cameraControl',
    label: 'Camera Control',
    maturity: 'mixed',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Install and remove the emulator settings needed by Camera Control.',
    games: swordShieldGames,
    icon: Cctv,
    id: 'cameraHook',
    label: 'Camera Hook',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Allow or block items from the player’s Bag during battle.',
    games: swordShieldGames,
    icon: Pill,
    id: 'battleItems',
    label: 'Battle Items',
    maturity: 'mixed',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Browse 3D models and play animations in the embedded DX12 viewport.',
    games: plaGames,
    icon: Box,
    id: 'modelViewer',
    label: '3D Model Editor',
    maturity: 'mixed',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'Browse game audio and listen in Sound Studio.',
    games: plaGames,
    icon: Music,
    id: 'soundStudio',
    label: 'Sound Studio',
    maturity: 'readOnly',
    showInWorkflowDashboard: false,
    standalone: true
  }),
  workflow({
    description: 'CSV, TSV, and JSON import profiles that execute through backend edit sessions.',
    games: allGames,
    icon: Upload,
    id: 'spreadsheetImport',
    label: 'Dump Importer'
  }),
  workflow({
    description:
      'Combine mods and choose how differences are resolved.',
    games: allGames,
    icon: GitMerge,
    id: 'modMerger',
    label: 'Mod Merger',
    standalone: true
  }),
  {
    capabilityKinds: ['navigation', 'command'],
    description: 'Review staged changes and apply a verified plan.',
    domain: 'workspace.changes',
    games: plaGames,
    icon: ClipboardCheck,
    id: 'changes',
    label: 'Changes',
    maturity: 'utility',
    navigationKind: 'utility',
    showInWorkflowDashboard: false,
    standalone: false
  },
  {
    capabilityKinds: ['navigation', 'command'],
    description: 'Browse dated output transactions and their recorded changes.',
    domain: 'workspace.history',
    games: plaGames,
    icon: History,
    id: 'history',
    label: 'History',
    maturity: 'utility',
    navigationKind: 'utility',
    showInWorkflowDashboard: false,
    standalone: false
  },
  {
    capabilityKinds: ['navigation', 'command'],
    description: 'Configure interface, cache, language, and update preferences.',
    domain: 'workspace.settings',
    games: plaGames,
    icon: Settings,
    id: 'settings',
    label: 'Settings',
    maturity: 'utility',
    navigationKind: 'utility',
    showInWorkflowDashboard: false,
    standalone: false
  }
] as const satisfies readonly WorkbenchCapabilityRegistration[];

const registrationBySection = new Map<WorkbenchSection, WorkbenchCapabilityRegistration>(
  workbenchCapabilityRegistry.map((registration) => [registration.id, registration])
);
const semanticContractKeyPattern = /^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u;

if (
  registrationBySection.size !== workbenchCapabilityRegistry.length ||
  workbenchSections.some((section) => !registrationBySection.has(section))
) {
  throw new Error('Every workbench section must have exactly one capability registration.');
}

if (
  workbenchCapabilityRegistry.some(
    (registration) =>
      registration.domain.length > 128 ||
      !semanticContractKeyPattern.test(registration.domain) ||
      registration.games.length === 0 ||
      new Set<ProjectGame>(registration.games).size !== registration.games.length ||
      registration.capabilityKinds.length === 0 ||
      new Set<CapabilityKind>(registration.capabilityKinds).size !==
        registration.capabilityKinds.length
  )
) {
  throw new Error('Workbench capability metadata violates its canonical contract.');
}

export function getWorkbenchCapabilityRegistration(section: WorkbenchSection) {
  const registration = registrationBySection.get(section);
  if (!registration) {
    throw new Error(`Workbench capability registration is missing for ${section}.`);
  }

  return registration;
}

export function isRegisteredWorkbenchSection(value: string): value is WorkbenchSection {
  return registrationBySection.has(value as WorkbenchSection);
}

export function getWorkbenchCapabilitiesByNavigationKind(
  navigationKind: CapabilityNavigationKindViewModel
) {
  return workbenchCapabilityRegistry.filter(
    (registration) => registration.navigationKind === navigationKind
  );
}

export function getWorkbenchSectionLabelKey(section: WorkbenchSection) {
  return workbenchSectionLocalization[section].label;
}

export function getWorkbenchSectionDescriptionKey(section: WorkbenchSection, game?: ProjectGame) {
  if (game === 'pla' && section === 'typeChart') return 'pla.typeChart.description';
  return workbenchSectionLocalization[section].description;
}

export function isCapabilityRegisteredForGame(
  section: WorkbenchSection,
  game: ProjectGame | null | undefined
) {
  return game !== null && game !== undefined && getWorkbenchCapabilityRegistration(section).games.includes(game);
}

export const workflowCapabilityRegistrations: readonly WorkbenchCapabilityRegistration[] =
  workbenchCapabilityRegistry.filter(
    (registration) =>
      registration.navigationKind === 'workflow' || registration.navigationKind === 'hidden'
  );

export const workflowDashboardRegistrations: readonly WorkbenchCapabilityRegistration[] =
  workflowCapabilityRegistrations.filter(
    (registration) => registration.showInWorkflowDashboard
  );

export const standaloneWorkflowSectionIds = new Set<WorkbenchSection>(
  workflowCapabilityRegistrations
    .filter((registration) => registration.standalone)
    .map((registration) => registration.id)
);

export const readOnlyViewerSectionIds = new Set<WorkbenchSection>(
  workflowCapabilityRegistrations
    .filter((registration) => registration.maturity === 'readOnly')
    .map((registration) => registration.id)
);

export const hiddenWorkflowSectionIds = new Set<WorkbenchSection>(
  workflowCapabilityRegistrations
    .filter((registration) => registration.navigationKind === 'hidden')
    .map((registration) => registration.id)
);
