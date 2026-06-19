/* SPDX-License-Identifier: GPL-3.0-only */
import {
  type BagHookWorkflow,
  type BehaviorWorkflow,
  type CatchCapWorkflow,
  type DynamaxAdventuresWorkflow,
  type EncountersWorkflow,
  type ExeFsPatchWorkflow,
  type FashionUnlockWorkflow,
  type FlagworkSaveWorkflow,
  type GiftPokemonWorkflow,
  type GymUniformRemovalWorkflow,
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
  type TextWorkflow,
  type TradePokemonWorkflow,
  type TrainersWorkflow,
  type TypeChartWorkflow,
  type WorkflowSummary
} from './bridge/contracts';
import { type HyperspaceBypassWorkflow } from './bridge/hyperspaceBypassContracts';
import { type FairyGymBoostsWorkflow } from './bridge/fairyGymBoostsContracts';
import { type NpcItemGiftWorkflow } from './bridge/npcItemGiftContracts';
import { type ShinyRateWorkflow } from './bridge/shinyRateContracts';
import { type WorkbenchSection } from './workbenchStore';

export type WorkflowNavigationGroup = {
  id:
    | 'viewers'
    | 'editors'
    | 'encountersPokemonSources'
    | 'economy'
    | 'tools'
    | 'hooks'
    | 'advancedEditors';
  label: string;
  sectionIds: WorkbenchSection[];
};

export const workflowNavigationGroups: WorkflowNavigationGroup[] = [
  { id: 'viewers', label: 'Viewers', sectionIds: ['flagworkSave', 'text'] },
  {
    id: 'editors',
    label: 'Editors',
    sectionIds: ['pokemon', 'trainers', 'moves', 'items', 'placement', 'behavior']
  },
  {
    id: 'encountersPokemonSources',
    label: 'Encounters & Pokemon Sources',
    sectionIds: ['encounters', 'staticEncounters', 'giftPokemon', 'tradePokemon', 'raidBattles']
  },
  { id: 'economy', label: 'Economy', sectionIds: ['shops', 'raidRewards', 'raidBonusRewards'] },
  { id: 'tools', label: 'Tools', sectionIds: ['fpsPatch', 'randomizer', 'modMerger', 'spreadsheetImport'] },
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
      'fairyGymBoosts',
      'fashionUnlock',
      'gymUniformRemoval',
      'hyperspaceBypass',
      'dynamaxAdventures'
    ]
  }
];

const swordShieldWorkflowSectionIds = new Set<WorkbenchSection>([
  'items',
  'pokemon',
  'moves',
  'text',
  'trainers',
  'giftPokemon',
  'tradePokemon',
  'staticEncounters',
  'rentalPokemon',
  'dynamaxAdventures',
  'shops',
  'encounters',
  'raidBattles',
  'raidRewards',
  'raidBonusRewards',
  'placement',
  'behavior',
  'flagworkSave',
  'bagHook',
  'catchCap',
  'hyperTraining',
  'shinyRate',
  'typeChart',
  'fairyGymBoosts',
  'fashionUnlock',
  'gymUniformRemoval',
  'ivScreen',
  'exefsPatches',
  'royalCandy',
  'startingItems',
  'npcItemGift',
  'spreadsheetImport',
  'modMerger',
  'fpsPatch',
  'randomizer'
]);

const scarletVioletWorkflowSectionIds = new Set<WorkbenchSection>([
  'items',
  'moves',
  'pokemon',
  'trainers',
  'encounters',
  'placement',
  'hyperspaceBypass',
  'modMerger'
]);

export const standaloneWorkflowSectionIds = new Set<WorkbenchSection>(['fpsPatch', 'randomizer']);

export const scarletVioletAdvancedEditorSectionIds = new Set<WorkbenchSection>([
  'hyperspaceBypass'
]);

export const scarletVioletAdvancedEditorDomains = new Set([
  'workflow.hyperspaceBypass'
]);

export const sharedStagedEditorSectionIds = new Set<WorkbenchSection>([
  'pokemon',
  'trainers',
  'moves',
  'items',
  'placement',
  'behavior',
  'encounters',
  'staticEncounters',
  'giftPokemon',
  'tradePokemon',
  'rentalPokemon',
  'raidBattles',
  'shops',
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
  'workflow.staticEncounters',
  'workflow.giftPokemon',
  'workflow.tradePokemon',
  'workflow.rentalPokemon',
  'workflow.raidBattles',
  'workflow.shops',
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

export type LoadedWorkflowStateBySection = {
  bagHookWorkflow: BagHookWorkflow | null;
  behaviorWorkflow: BehaviorWorkflow | null;
  catchCapWorkflow: CatchCapWorkflow | null;
  dynamaxAdventuresWorkflow: DynamaxAdventuresWorkflow | null;
  encountersWorkflow: EncountersWorkflow | null;
  exeFsPatchWorkflow: ExeFsPatchWorkflow | null;
  fairyGymBoostsWorkflow: FairyGymBoostsWorkflow | null;
  fashionUnlockWorkflow: FashionUnlockWorkflow | null;
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
  spreadsheetImportWorkflow: SpreadsheetImportWorkflow | null;
  startingItemsWorkflow: StartingItemsWorkflow | null;
  staticEncountersWorkflow: StaticEncountersWorkflow | null;
  svModMergerWorkflow: SvModMergerWorkflow | null;
  textWorkflow: TextWorkflow | null;
  tradePokemonWorkflow: TradePokemonWorkflow | null;
  trainersWorkflow: TrainersWorkflow | null;
  typeChartWorkflow: TypeChartWorkflow | null;
};

function getGameWorkflowSectionIds(game: ProjectGame | null | undefined) {
  if (isScarletVioletGame(game)) {
    return scarletVioletWorkflowSectionIds;
  }

  if (game === 'sword' || game === 'shield') {
    return swordShieldWorkflowSectionIds;
  }

  return new Set<WorkbenchSection>();
}

export function isScarletVioletGame(game: ProjectGame | null | undefined) {
  return game === 'scarlet' || game === 'violet';
}

export function isWorkflowSupportedForGame(
  section: WorkbenchSection,
  game: ProjectGame | null | undefined
) {
  return getGameWorkflowSectionIds(game).has(section);
}

export function getGameScopedWorkflowSummaries(
  workflows: WorkflowSummary[],
  game: ProjectGame | null | undefined
) {
  const supportedSectionIds = getGameWorkflowSectionIds(game);
  return workflows.filter((workflow) =>
    supportedSectionIds.has(workflow.id as WorkbenchSection)
  );
}

export function getLoadedWorkflowStateForSection(
  section: WorkbenchSection,
  state: LoadedWorkflowStateBySection
) {
  switch (section) {
    case 'bagHook':
      return state.bagHookWorkflow !== null;
    case 'behavior':
      return state.behaviorWorkflow !== null;
    case 'catchCap':
      return state.catchCapWorkflow !== null;
    case 'dynamaxAdventures':
      return state.dynamaxAdventuresWorkflow !== null;
    case 'encounters':
      return state.encountersWorkflow !== null;
    case 'exefsPatches':
      return state.exeFsPatchWorkflow !== null;
    case 'fairyGymBoosts':
      return state.fairyGymBoostsWorkflow !== null;
    case 'fashionUnlock':
      return state.fashionUnlockWorkflow !== null;
    case 'flagworkSave':
      return state.flagworkSaveWorkflow !== null;
    case 'giftPokemon':
      return state.giftPokemonWorkflow !== null;
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
    case 'typeChart':
      return state.typeChartWorkflow !== null;
    default:
      return false;
  }
}

export function isWorkflowSection(section: WorkbenchSection) {
  return (
    swordShieldWorkflowSectionIds.has(section) ||
    scarletVioletWorkflowSectionIds.has(section)
  );
}

export function isWorkflowNavigationVisibleForGame(
  section: WorkbenchSection,
  game: ProjectGame | null | undefined,
  availableWorkflowSectionIds: ReadonlySet<WorkbenchSection>
) {
  return (
    isWorkflowSupportedForGame(section, game) &&
    (availableWorkflowSectionIds.has(section) || standaloneWorkflowSectionIds.has(section))
  );
}
