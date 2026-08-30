/* SPDX-License-Identifier: GPL-3.0-only */
import {
  type BagHookWorkflow,
  type BehaviorWorkflow,
  type CatchCapWorkflow,
  type DynamaxAdventuresWorkflow,
  type EncountersWorkflow,
  type ExeFsPatchWorkflow,
  type FlagworkSaveWorkflow,
  type GiftPokemonWorkflow,
  type HyperTrainingWorkflow,
  type ItemsWorkflow,
  type IvScreenWorkflow,
  type ModMergerWorkflow,
  type MovesWorkflow,
  type PlacementWorkflow,
  type PokemonWorkflow,
  type ProjectGame,
  type RaidBattlesWorkflow,
  type RaidRewardsWorkflow,
  type RentalPokemonWorkflow,
  type RoyalCandyWorkflow,
  type ShopsWorkflow,
  type SpreadsheetImportWorkflow,
  type StartingItemsWorkflow,
  type StaticEncountersWorkflow,
  type SvModMergerWorkflow,
  type TeraRaidsWorkflow,
  type TextWorkflow,
  type TradePokemonWorkflow,
  type TrainersWorkflow,
  type TypeChartWorkflow,
  type ZaModMergerWorkflow,
  type WorkflowSummary
} from './bridge/contracts';
import { type AngeFightWorkflow } from './bridge/angeFightContracts';
import { type BattleCafeRewardsWorkflow } from './bridge/battleCafeRewardsContracts';
import { type FashionUnlockWorkflow } from './bridge/fashionUnlockContracts';
import { type FashionCatalogWorkflow } from './bridge/fashionCatalogContracts';
import { type GymUniformRemovalWorkflow } from './bridge/gymUniformRemovalContracts';
import { type HabitatCoordinatesWorkflow } from './bridge/habitatCoordinatesContracts';
import { type HyperspaceBypassWorkflow } from './bridge/hyperspaceBypassContracts';
import { type FairyGymBoostsWorkflow } from './bridge/fairyGymBoostsContracts';
import { type NpcItemGiftWorkflow } from './bridge/npcItemGiftContracts';
import { type ShinyRateWorkflow } from './bridge/shinyRateContracts';
import { type TmMachineControlsWorkflow } from './bridge/tmMachineControlsContracts';
import { type TrainerPoolsWorkflow } from './bridge/trainerPoolsContracts';
import { type WorkbenchSection } from './workbenchStore';
import {
  getWorkbenchCapabilityRegistration,
  hiddenWorkflowSectionIds,
  isCapabilityRegisteredForGame,
  isRegisteredWorkbenchSection,
  readOnlyViewerSectionIds,
  standaloneWorkflowSectionIds
} from './workbench/capabilityRegistry';

export { readOnlyViewerSectionIds, standaloneWorkflowSectionIds } from './workbench/capabilityRegistry';

export type WorkflowNavigationGroup = {
  id:
    | 'viewers'
    | 'editors'
    | 'encountersPokemonSources'
    | 'economy'
    | 'tools'
    | 'hooks'
    | 'advancedEditors'
    | 'betaEditors';
  label: string;
  labelKey?: string;
  sectionIds: WorkbenchSection[];
};

export const workflowNavigationGroups: WorkflowNavigationGroup[] = [
  { id: 'viewers', label: 'Viewers', sectionIds: ['flagworkSave'] },
  {
    id: 'editors',
    label: 'Editors',
    sectionIds: ['pokemon', 'trainers', 'trainerPools', 'moves', 'items', 'placement', 'behavior', 'text']
  },
  {
    id: 'encountersPokemonSources',
    label: 'Encounters & Pokemon Sources',
    sectionIds: [
      'encounters',
      'habitatCoordinates',
      'staticEncounters',
      'giftPokemon',
      'tradePokemon',
      'rentalPokemon',
      'teraRaids',
      'raidBattles'
    ]
  },
  {
    id: 'economy',
    label: 'Economy',
    sectionIds: ['shops', 'battleCafeRewards', 'tmMachineControls', 'raidRewards', 'raidBonusRewards']
  },
  { id: 'tools', label: 'Tools', sectionIds: ['fashionCatalog', 'fpsPatch', 'profanityFilter', 'randomizer', 'gameDump', 'spreadsheetImport', 'modMerger'] },
  { id: 'hooks', label: 'Hooks', sectionIds: ['bagHook'] },
  {
    id: 'advancedEditors',
    label: 'Advanced Editors',
    sectionIds: [
      'royalCandy',
      'startingItems',
      'npcItemGift',
      'catchCap',
      'ivScreen',
      'hyperTraining',
      'shinyRate',
      'typeChart',
      'dexLayout',
      'angeFight',
      'fairyGymBoosts',
      'fashionUnlock',
      'gymUniformRemoval',
      'hyperspaceBypass',
      'dynamaxAdventures'
    ]
  },
  {
    id: 'betaEditors',
    label: 'Beta Editors',
    labelKey: 'workbench.navigation.betaEditors',
    sectionIds: ['gameplaySettings']
  }
];

export function canAccessWorkflowSectionForHealth(
  section: WorkbenchSection,
  canOpenReadOnlyWorkflows: boolean,
  canOpenEditableWorkflows: boolean
) {
  if (section === 'gameplaySettings') {
    return true;
  }

  return (
    canOpenEditableWorkflows ||
    (canOpenReadOnlyWorkflows && readOnlyViewerSectionIds.has(section))
  );
}

export const scarletVioletAdvancedEditorSectionIds = new Set<WorkbenchSection>([
  'typeChart',
  'fashionUnlock',
  'hyperspaceBypass'
]);

export const scarletVioletAdvancedEditorDomains = new Set([
  'workflow.typeChart',
  'workflow.fashionUnlock',
  'workflow.hyperspaceBypass'
]);

export const pokemonLegendsZAAdvancedEditorSectionIds = new Set<WorkbenchSection>([
  'angeFight',
  'dexLayout'
]);

export const pokemonLegendsZAAdvancedEditorDomains = new Set([
  'workflow.angeFight'
]);

export const sharedStagedEditorSectionIds = new Set<WorkbenchSection>([
  'pokemon',
  'trainers',
  'moves',
  'items',
  'placement',
  'behavior',
  'encounters',
  'teraRaids',
  'staticEncounters',
  'giftPokemon',
  'tradePokemon',
  'rentalPokemon',
  'raidBattles',
  'shops',
  'tmMachineControls',
  'raidRewards',
  'raidBonusRewards',
  'text'
]);

export const sharedStagedEditorDomains = new Set([
  'workflow.pokemon',
  'workflow.trainers',
  'workflow.moves',
  'workflow.items',
  'workflow.placement',
  'workflow.behavior',
  'workflow.encounters',
  'workflow.teraRaids',
  'workflow.staticEncounters',
  'workflow.giftPokemon',
  'workflow.tradePokemon',
  'workflow.rentalPokemon',
  'workflow.raidBattles',
  'workflow.shops',
  'workflow.tmMachineControls',
  'workflow.raidRewards',
  'workflow.raidBonusRewards',
  'workflow.text'
]);

export function isSharedStagedEditorSection(
  section: WorkbenchSection,
  game: ProjectGame | null | undefined
) {
  return sharedStagedEditorSectionIds.has(section) && isWorkflowSupportedForGame(section, game);
}

export function isScarletVioletAdvancedEditorSection(
  section: WorkbenchSection | null,
  game: ProjectGame | null | undefined
) {
  return (
    section !== null &&
    isScarletVioletGame(game) &&
    scarletVioletAdvancedEditorSectionIds.has(section)
  );
}

export function isPokemonLegendsZAAdvancedEditorSection(
  section: WorkbenchSection | null,
  game: ProjectGame | null | undefined
) {
  return (
    section !== null &&
    isPokemonLegendsZAGame(game) &&
    pokemonLegendsZAAdvancedEditorSectionIds.has(section)
  );
}

export type LoadedWorkflowStateBySection = {
  angeFightWorkflow: AngeFightWorkflow | null;
  bagHookWorkflow: BagHookWorkflow | null;
  battleCafeRewardsWorkflow: BattleCafeRewardsWorkflow | null;
  behaviorWorkflow: BehaviorWorkflow | null;
  catchCapWorkflow: CatchCapWorkflow | null;
  dynamaxAdventuresWorkflow: DynamaxAdventuresWorkflow | null;
  encountersWorkflow: EncountersWorkflow | null;
  exeFsPatchWorkflow: ExeFsPatchWorkflow | null;
  fairyGymBoostsWorkflow: FairyGymBoostsWorkflow | null;
  fashionUnlockWorkflow: FashionUnlockWorkflow | null;
  fashionCatalogWorkflow: FashionCatalogWorkflow | null;
  flagworkSaveWorkflow: FlagworkSaveWorkflow | null;
  giftPokemonWorkflow: GiftPokemonWorkflow | null;
  gymUniformRemovalWorkflow: GymUniformRemovalWorkflow | null;
  hyperTrainingWorkflow: HyperTrainingWorkflow | null;
  hyperspaceBypassWorkflow: HyperspaceBypassWorkflow | null;
  itemsWorkflow: ItemsWorkflow | null;
  ivScreenWorkflow: IvScreenWorkflow | null;
  modMergerWorkflow: ModMergerWorkflow | null;
  movesWorkflow: MovesWorkflow | null;
  npcItemGiftWorkflow: NpcItemGiftWorkflow | null;
  placementWorkflow: PlacementWorkflow | null;
  pokemonWorkflow: PokemonWorkflow | null;
  raidBattlesWorkflow: RaidBattlesWorkflow | null;
  raidBonusRewardsWorkflow: RaidRewardsWorkflow | null;
  raidRewardsWorkflow: RaidRewardsWorkflow | null;
  rentalPokemonWorkflow: RentalPokemonWorkflow | null;
  royalCandyWorkflow: RoyalCandyWorkflow | null;
  selectedGame: ProjectGame | null;
  shinyRateWorkflow: ShinyRateWorkflow | null;
  shopsWorkflow: ShopsWorkflow | null;
  tmMachineControlsWorkflow: TmMachineControlsWorkflow | null;
  habitatCoordinatesWorkflow: HabitatCoordinatesWorkflow | null;
  spreadsheetImportWorkflow: SpreadsheetImportWorkflow | null;
  startingItemsWorkflow: StartingItemsWorkflow | null;
  staticEncountersWorkflow: StaticEncountersWorkflow | null;
  svModMergerWorkflow: SvModMergerWorkflow | null;
  teraRaidsWorkflow: TeraRaidsWorkflow | null;
  textWorkflow: TextWorkflow | null;
  tradePokemonWorkflow: TradePokemonWorkflow | null;
  trainersWorkflow: TrainersWorkflow | null;
  trainerPoolsWorkflow: TrainerPoolsWorkflow | null;
  typeChartWorkflow: TypeChartWorkflow | null;
  zaModMergerWorkflow: ZaModMergerWorkflow | null;
};

export function isScarletVioletGame(game: ProjectGame | null | undefined) {
  return game === 'scarlet' || game === 'violet';
}

export function isPokemonLegendsZAGame(game: ProjectGame | null | undefined) {
  return game === 'za';
}

export function isTrinityCacheGame(game: ProjectGame | null | undefined) {
  return isScarletVioletGame(game) || isPokemonLegendsZAGame(game);
}

export function isWorkflowSupportedForGame(
  section: WorkbenchSection,
  game: ProjectGame | null | undefined
) {
  return isWorkflowSection(section) && isCapabilityRegisteredForGame(section, game);
}

export function getGameScopedWorkflowSummaries(
  workflows: WorkflowSummary[],
  game: ProjectGame | null | undefined
) {
  return workflows.filter((workflow): workflow is WorkflowSummary & { id: WorkbenchSection } => {
    if (!isRegisteredWorkbenchSection(workflow.id)) {
      return false;
    }

    return (
      isWorkflowSupportedForGame(workflow.id, game) &&
      !hiddenWorkflowSectionIds.has(workflow.id)
    );
  });
}

export function getLoadedWorkflowStateForSection(
  section: WorkbenchSection,
  state: LoadedWorkflowStateBySection
) {
  switch (section) {
    case 'angeFight':
      return state.angeFightWorkflow !== null;
    case 'bagHook':
      return state.bagHookWorkflow !== null;
    case 'battleCafeRewards':
      return state.battleCafeRewardsWorkflow !== null;
    case 'behavior':
      return state.behaviorWorkflow !== null;
    case 'catchCap':
      return state.catchCapWorkflow !== null;
    case 'dynamaxAdventures':
      return state.dynamaxAdventuresWorkflow !== null;
    case 'dexLayout':
      return state.pokemonWorkflow !== null;
    case 'encounters':
      return state.encountersWorkflow !== null;
    case 'exefsPatches':
      return state.exeFsPatchWorkflow !== null;
    case 'fairyGymBoosts':
      return state.fairyGymBoostsWorkflow !== null;
    case 'fashionUnlock':
      return state.fashionUnlockWorkflow !== null;
    case 'fashionCatalog':
      return state.fashionCatalogWorkflow !== null;
    case 'flagworkSave':
      return state.flagworkSaveWorkflow !== null;
    case 'giftPokemon':
      return state.giftPokemonWorkflow !== null;
    case 'gameplaySettings':
      return true;
    case 'gymUniformRemoval':
      return state.gymUniformRemovalWorkflow !== null;
    case 'hyperTraining':
      return state.hyperTrainingWorkflow !== null;
    case 'hyperspaceBypass':
      return state.hyperspaceBypassWorkflow !== null;
    case 'items':
      return state.itemsWorkflow !== null;
    case 'ivScreen':
      return state.ivScreenWorkflow !== null;
    case 'modMerger':
      return isScarletVioletGame(state.selectedGame)
        ? state.svModMergerWorkflow !== null
        : isPokemonLegendsZAGame(state.selectedGame)
          ? state.zaModMergerWorkflow !== null
          : state.modMergerWorkflow !== null;
    case 'moves':
      return state.movesWorkflow !== null;
    case 'npcItemGift':
      return state.npcItemGiftWorkflow !== null;
    case 'placement':
      return state.placementWorkflow !== null;
    case 'pokemon':
      return state.pokemonWorkflow !== null;
    case 'raidBattles':
      return state.raidBattlesWorkflow !== null;
    case 'teraRaids':
      return state.teraRaidsWorkflow !== null;
    case 'raidBonusRewards':
      return state.raidBonusRewardsWorkflow !== null;
    case 'raidRewards':
      return state.raidRewardsWorkflow !== null;
    case 'rentalPokemon':
      return state.rentalPokemonWorkflow !== null;
    case 'royalCandy':
      return state.royalCandyWorkflow !== null;
    case 'shinyRate':
      return state.shinyRateWorkflow !== null;
    case 'shops':
      return state.shopsWorkflow !== null;
    case 'tmMachineControls':
      return state.tmMachineControlsWorkflow !== null;
    case 'habitatCoordinates':
      return state.habitatCoordinatesWorkflow !== null;
    case 'spreadsheetImport':
      return state.spreadsheetImportWorkflow !== null;
    case 'startingItems':
      return state.startingItemsWorkflow !== null;
    case 'staticEncounters':
      return state.staticEncountersWorkflow !== null;
    case 'text':
      return state.textWorkflow !== null;
    case 'tradePokemon':
      return state.tradePokemonWorkflow !== null;
    case 'trainers':
      return state.trainersWorkflow !== null;
    case 'trainerPools':
      return state.trainerPoolsWorkflow !== null;
    case 'typeChart':
      return state.typeChartWorkflow !== null;
    default:
      return false;
  }
}

export function isWorkflowSection(section: WorkbenchSection) {
  const navigationKind = getWorkbenchCapabilityRegistration(section).navigationKind;
  return navigationKind === 'workflow' || navigationKind === 'hidden';
}

export function resolveWorkflowDataSection(section: WorkbenchSection): WorkbenchSection {
  return section === 'dexLayout' ? 'pokemon' : section;
}

export function isWorkflowNavigationVisibleForGame(
  section: WorkbenchSection,
  game: ProjectGame | null | undefined,
  availableWorkflowSectionIds: ReadonlySet<WorkbenchSection>
) {
  return (
    !hiddenWorkflowSectionIds.has(section) &&
    isWorkflowSupportedForGame(section, game) &&
    (
      availableWorkflowSectionIds.has(resolveWorkflowDataSection(section)) ||
      standaloneWorkflowSectionIds.has(section)
    )
  );
}
