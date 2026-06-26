/* SPDX-License-Identifier: GPL-3.0-only */
import { create } from 'zustand';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type BagHookWorkflow,
  type BehaviorWorkflow,
  type CatchCapWorkflow,
  type ChangePlan,
  type DynamaxAdventuresWorkflow,
  type EditSession,
  type EncounterTableRecord,
  type EncountersWorkflow,
  type ExeFsPatchWorkflow,
  type FashionUnlockWorkflow,
  type FlagworkSaveWorkflow,
  type GiftPokemonWorkflow,
  type GymUniformRemovalWorkflow,
  type HyperTrainingWorkflow,
  type ItemsWorkflow,
  type IvScreenWorkflow,
  type MovesWorkflow,
  type PlacementWorkflow,
  type PokemonWorkflow,
  type ProjectGame,
  type ProjectFileGraph,
  type ProjectHealth,
  type RaidBattlesWorkflow,
  type RaidRewardsWorkflow,
  type RentalPokemonWorkflow,
  type RoyalCandyWorkflow,
  type SpreadsheetImportPreview,
  type SpreadsheetImportWorkflow,
  type StartingItemsWorkflow,
  type StaticEncountersWorkflow,
  type ShopsWorkflow,
  type TeraRaidsWorkflow,
  type TextWorkflow,
  type TypeChartWorkflow,
  type TradePokemonWorkflow,
  type TrainersWorkflow,
  type WorkflowSummary
} from './bridge/contracts';
import { type FairyGymBoostsWorkflow } from './bridge/fairyGymBoostsContracts';
import { type HyperspaceBypassWorkflow } from './bridge/hyperspaceBypassContracts';
import { type NpcItemGiftWorkflow } from './bridge/npcItemGiftContracts';
import { type ShinyRateWorkflow } from './bridge/shinyRateContracts';
export type WorkbenchSection =
  | 'health'
  | 'workflows'
  | 'items'
  | 'pokemon'
  | 'moves'
  | 'text'
  | 'trainers'
  | 'giftPokemon'
  | 'tradePokemon'
  | 'staticEncounters'
  | 'rentalPokemon'
  | 'dynamaxAdventures'
  | 'shops'
  | 'encounters'
  | 'teraRaids'
  | 'raidBattles'
  | 'raidRewards'
  | 'raidBonusRewards'
  | 'placement'
  | 'behavior'
  | 'flagworkSave'
  | 'bagHook'
  | 'catchCap'
  | 'hyperTraining'
  | 'shinyRate'
  | 'typeChart'
  | 'fairyGymBoosts'
  | 'fashionUnlock' | 'gymUniformRemoval' | 'hyperspaceBypass' | 'ivScreen'
  | 'exefsPatches'
  | 'royalCandy' | 'startingItems' | 'npcItemGift'
  | 'spreadsheetImport'
  | 'modMerger'
  | 'fpsPatch' | 'randomizer' | 'gameDump'
  | 'changes'
  | 'settings';
export type ProjectPathDraft = {
  baseExeFsPath: string;
  baseRomFsPath: string;
  outputRootPath: string;
  pokemonLegendsZASupportFolderPath: string;
  saveFilePath: string;
  scarletVioletSupportFolderPath: string;
  selectedGame: ProjectGame | null;
};
export type ProjectPathFieldName = Exclude<keyof ProjectPathDraft, 'selectedGame'>;
type ProjectPathDraftValues = Pick<ProjectPathDraft, ProjectPathFieldName>;
type ValidatedProjectPathCache = Partial<Record<ProjectGame, ProjectPathDraftValues>>;
const projectPathDraftStorageKey = 'km-editor.project-path-draft.v1';
const validatedProjectPathCacheStorageKey = 'km-editor.validated-project-path-cache.v1';
export type OpenProjectState = {
  fileGraph: ProjectFileGraph;
  health: ProjectHealth;
  projectId: string;
};
type WorkbenchState = {
  activeSection: WorkbenchSection;
  applyResult: ApplyResult | null;
  changePlan: ChangePlan | null;
  draftPaths: ProjectPathDraft;
  editSession: EditSession | null;
  editValidationDiagnostics: ApiDiagnostic[];
  bagHookWorkflow: BagHookWorkflow | null;
  encounterSearchText: string;
  encountersWorkflow: EncountersWorkflow | null;
  catchCapWorkflow: CatchCapWorkflow | null;
  hyperTrainingWorkflow: HyperTrainingWorkflow | null;
  shinyRateWorkflow: ShinyRateWorkflow | null;
  typeChartWorkflow: TypeChartWorkflow | null;
  fairyGymBoostsWorkflow: FairyGymBoostsWorkflow | null;
  fashionUnlockWorkflow: FashionUnlockWorkflow | null; gymUniformRemovalWorkflow: GymUniformRemovalWorkflow | null; hyperspaceBypassWorkflow: HyperspaceBypassWorkflow | null; ivScreenWorkflow: IvScreenWorkflow | null;
  exeFsPatchSearchText: string;
  exeFsPatchWorkflow: ExeFsPatchWorkflow | null;
  flagworkSaveSearchText: string;
  flagworkSaveWorkflow: FlagworkSaveWorkflow | null;
  giftPokemonSearchText: string;
  giftPokemonWorkflow: GiftPokemonWorkflow | null;
  tradePokemonSearchText: string; tradePokemonWorkflow: TradePokemonWorkflow | null;
  staticEncounterSearchText: string;
  staticEncountersWorkflow: StaticEncountersWorkflow | null;
  rentalPokemonSearchText: string;
  rentalPokemonWorkflow: RentalPokemonWorkflow | null;
  dynamaxAdventureSearchText: string;
  dynamaxAdventuresWorkflow: DynamaxAdventuresWorkflow | null;
  itemSearchText: string;
  itemsWorkflow: ItemsWorkflow | null;
  movesSearchText: string;
  movesWorkflow: MovesWorkflow | null;
  openProject: OpenProjectState | null;
  behaviorSearchText: string;
  behaviorWorkflow: BehaviorWorkflow | null;
  placementSearchText: string;
  placementWorkflow: PlacementWorkflow | null;
  pokemonSearchText: string;
  pokemonWorkflow: PokemonWorkflow | null;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
  teraRaidSearchText: string;
  teraRaidsWorkflow: TeraRaidsWorkflow | null;
  raidBattleSearchText: string;
  raidBattlesWorkflow: RaidBattlesWorkflow | null;
  raidRewardSearchText: string;
  raidRewardsWorkflow: RaidRewardsWorkflow | null;
  raidBonusRewardSearchText: string;
  raidBonusRewardsWorkflow: RaidRewardsWorkflow | null;
  royalCandySearchText: string;
  royalCandyWorkflow: RoyalCandyWorkflow | null;
  startingItemsWorkflow: StartingItemsWorkflow | null; npcItemGiftWorkflow: NpcItemGiftWorkflow | null;
  spreadsheetImportPreview: SpreadsheetImportPreview | null;
  spreadsheetImportSearchText: string;
  spreadsheetImportSourcePath: string;
  spreadsheetImportWorkflow: SpreadsheetImportWorkflow | null;
  selectedRaidRewardTableId: string | null;
  selectedRaidBonusRewardTableId: string | null;
  selectedBehaviorEntryId: string | null;
  selectedPlacementObjectId: string | null;
  selectedFlagId: string | null;
  selectedSaveBlockId: string | null;
  selectedBagHookSlot: number | null;
  selectedExeFsCheckId: string | null;
  selectedExeFsPatchId: string | null;
  selectedCatchCapBadgeCount: number | null;
  selectedGiftPokemonIndex: number | null;
  selectedTradePokemonIndex: number | null;
  selectedStaticEncounterIndex: number | null;
  selectedRentalPokemonIndex: number | null;
  selectedDynamaxAdventureEntryIndex: number | null;
  selectedRoyalCandyCheckId: string | null;
  selectedRoyalCandyWorkflowId: string | null;
  selectedStartingItemSlot: number | null;
  selectedSpreadsheetImportProfileId: string | null;
  selectedItemId: number | null;
  selectedMoveId: number | null;
  selectedPokemonPersonalId: number | null;
  selectedEncounterTableId: string | null;
  selectedTeraRaidRecordId: string | null;
  selectedRaidBattleTableId: string | null;
  selectedShopId: string | null;
  selectedTextKey: string | null;
  selectedTrainerId: number | null;
  shopSearchText: string;
  shopsWorkflow: ShopsWorkflow | null;
  textSearchText: string;
  textWorkflow: TextWorkflow | null;
  trainerSearchText: string;
  trainersWorkflow: TrainersWorkflow | null;
  workflows: WorkflowSummary[];
  setDraftPath: (field: ProjectPathFieldName, value: string) => void;
  setActiveSection: (activeSection: WorkbenchSection) => void;
  setApplyResult: (applyResult: ApplyResult | null) => void;
  setChangePlan: (changePlan: ChangePlan | null) => void;
  setEditSession: (editSession: EditSession | null) => void;
  setEditValidationDiagnostics: (diagnostics: ApiDiagnostic[]) => void;
  setBagHookWorkflow: (bagHookWorkflow: BagHookWorkflow) => void;
  setEncounterSearchText: (encounterSearchText: string) => void;
  setEncountersWorkflow: (encountersWorkflow: EncountersWorkflow) => void;
  setCatchCapWorkflow: (catchCapWorkflow: CatchCapWorkflow) => void;
  setHyperTrainingWorkflow: (hyperTrainingWorkflow: HyperTrainingWorkflow) => void;
  setShinyRateWorkflow: (shinyRateWorkflow: ShinyRateWorkflow) => void;
  setTypeChartWorkflow: (typeChartWorkflow: TypeChartWorkflow) => void;
  setFairyGymBoostsWorkflow: (fairyGymBoostsWorkflow: FairyGymBoostsWorkflow) => void;
  setFashionUnlockWorkflow: (fashionUnlockWorkflow: FashionUnlockWorkflow) => void;
  setGymUniformRemovalWorkflow: (
    gymUniformRemovalWorkflow: GymUniformRemovalWorkflow
  ) => void;
  setHyperspaceBypassWorkflow: (hyperspaceBypassWorkflow: HyperspaceBypassWorkflow) => void;
  setIvScreenWorkflow: (ivScreenWorkflow: IvScreenWorkflow) => void;
  setExeFsPatchSearchText: (exeFsPatchSearchText: string) => void;
  setExeFsPatchWorkflow: (exeFsPatchWorkflow: ExeFsPatchWorkflow) => void;
  setFlagworkSaveSearchText: (flagworkSaveSearchText: string) => void;
  setFlagworkSaveWorkflow: (flagworkSaveWorkflow: FlagworkSaveWorkflow) => void;
  setGiftPokemonSearchText: (giftPokemonSearchText: string) => void;
  setGiftPokemonWorkflow: (giftPokemonWorkflow: GiftPokemonWorkflow) => void;
  setTradePokemonSearchText: (tradePokemonSearchText: string) => void;
  setTradePokemonWorkflow: (tradePokemonWorkflow: TradePokemonWorkflow) => void;
  setStaticEncounterSearchText: (staticEncounterSearchText: string) => void;
  setStaticEncountersWorkflow: (staticEncountersWorkflow: StaticEncountersWorkflow) => void;
  setRentalPokemonSearchText: (rentalPokemonSearchText: string) => void;
  setRentalPokemonWorkflow: (rentalPokemonWorkflow: RentalPokemonWorkflow) => void;
  setDynamaxAdventureSearchText: (dynamaxAdventureSearchText: string) => void;
  setDynamaxAdventuresWorkflow: (
    dynamaxAdventuresWorkflow: DynamaxAdventuresWorkflow
  ) => void;
  setItemsWorkflow: (itemsWorkflow: ItemsWorkflow) => void;
  setItemSearchText: (itemSearchText: string) => void;
  setMovesSearchText: (movesSearchText: string) => void;
  setMovesWorkflow: (movesWorkflow: MovesWorkflow) => void;
  setOpenProject: (project: OpenProjectState) => void;
  setBehaviorSearchText: (behaviorSearchText: string) => void;
  setBehaviorWorkflow: (behaviorWorkflow: BehaviorWorkflow) => void;
  setPlacementSearchText: (placementSearchText: string) => void;
  setPlacementWorkflow: (placementWorkflow: PlacementWorkflow) => void;
  setPokemonSearchText: (pokemonSearchText: string) => void;
  setPokemonWorkflow: (pokemonWorkflow: PokemonWorkflow) => void;
  setProjectHealth: (health: ProjectHealth) => void;
  setProjectStatus: (projectStatus: WorkbenchState['projectStatus']) => void;
  setTeraRaidSearchText: (teraRaidSearchText: string) => void;
  setTeraRaidsWorkflow: (teraRaidsWorkflow: TeraRaidsWorkflow) => void;
  setRaidBattleSearchText: (raidBattleSearchText: string) => void;
  setRaidBattlesWorkflow: (raidBattlesWorkflow: RaidBattlesWorkflow) => void;
  setRaidRewardSearchText: (raidRewardSearchText: string) => void;
  setRaidRewardsWorkflow: (raidRewardsWorkflow: RaidRewardsWorkflow) => void;
  setRaidBonusRewardSearchText: (raidBonusRewardSearchText: string) => void;
  setRaidBonusRewardsWorkflow: (raidBonusRewardsWorkflow: RaidRewardsWorkflow) => void;
  setRoyalCandySearchText: (royalCandySearchText: string) => void;
  setRoyalCandyWorkflow: (royalCandyWorkflow: RoyalCandyWorkflow) => void;
  setStartingItemsWorkflow: (startingItemsWorkflow: StartingItemsWorkflow) => void; setNpcItemGiftWorkflow: (npcItemGiftWorkflow: NpcItemGiftWorkflow) => void;
  setSpreadsheetImportPreview: (preview: SpreadsheetImportPreview | null) => void;
  setSpreadsheetImportSearchText: (spreadsheetImportSearchText: string) => void;
  setSpreadsheetImportSourcePath: (sourcePath: string) => void;
  setSpreadsheetImportWorkflow: (spreadsheetImportWorkflow: SpreadsheetImportWorkflow) => void;
  setSelectedRaidRewardTableId: (selectedRaidRewardTableId: string | null) => void;
  setSelectedRaidBonusRewardTableId: (selectedRaidBonusRewardTableId: string | null) => void;
  setSelectedBehaviorEntryId: (selectedBehaviorEntryId: string | null) => void;
  setSelectedPlacementObjectId: (selectedPlacementObjectId: string | null) => void;
  setSelectedFlagId: (selectedFlagId: string | null) => void;
  setSelectedSaveBlockId: (selectedSaveBlockId: string | null) => void;
  setSelectedBagHookSlot: (selectedBagHookSlot: number | null) => void;
  setSelectedExeFsCheckId: (selectedExeFsCheckId: string | null) => void;
  setSelectedExeFsPatchId: (selectedExeFsPatchId: string | null) => void;
  setSelectedCatchCapBadgeCount: (selectedCatchCapBadgeCount: number | null) => void;
  setSelectedGiftPokemonIndex: (selectedGiftPokemonIndex: number | null) => void;
  setSelectedTradePokemonIndex: (selectedTradePokemonIndex: number | null) => void;
  setSelectedStaticEncounterIndex: (selectedStaticEncounterIndex: number | null) => void;
  setSelectedRentalPokemonIndex: (selectedRentalPokemonIndex: number | null) => void;
  setSelectedDynamaxAdventureEntryIndex: (
    selectedDynamaxAdventureEntryIndex: number | null
  ) => void;
  setSelectedRoyalCandyCheckId: (selectedRoyalCandyCheckId: string | null) => void;
  setSelectedRoyalCandyWorkflowId: (selectedRoyalCandyWorkflowId: string | null) => void;
  setSelectedStartingItemSlot: (selectedStartingItemSlot: number | null) => void;
  setSelectedSpreadsheetImportProfileId: (
    selectedSpreadsheetImportProfileId: string | null
  ) => void;
  setSelectedItemId: (selectedItemId: number | null) => void;
  setSelectedMoveId: (selectedMoveId: number | null) => void;
  setSelectedPokemonPersonalId: (selectedPokemonPersonalId: number | null) => void;
  setSelectedEncounterTableId: (selectedEncounterTableId: string | null) => void;
  setSelectedTeraRaidRecordId: (selectedTeraRaidRecordId: string | null) => void;
  setSelectedRaidBattleTableId: (selectedRaidBattleTableId: string | null) => void;
  setSelectedShopId: (selectedShopId: string | null) => void;
  setSelectedTextKey: (selectedTextKey: string | null) => void;
  setSelectedTrainerId: (selectedTrainerId: number | null) => void;
  setShopSearchText: (shopSearchText: string) => void;
  setShopsWorkflow: (shopsWorkflow: ShopsWorkflow) => void;
  setTextSearchText: (textSearchText: string) => void;
  setTextWorkflow: (textWorkflow: TextWorkflow) => void;
  setTrainerSearchText: (trainerSearchText: string) => void;
  setTrainersWorkflow: (trainersWorkflow: TrainersWorkflow) => void;
  setWorkflows: (workflows: WorkflowSummary[]) => void;
  clearSelectedGame: () => void;
  rememberValidatedProjectPaths: (draftPaths: ProjectPathDraft) => void;
  setSelectedGame: (selectedGame: ProjectGame) => void;
};
function resolveWorkflowLoadSection(
  activeSection: WorkbenchSection,
  workflowSection: WorkbenchSection
) {
  return activeSection === 'workflows' ? workflowSection : activeSection;
}
function resolveSelectedPokemonPersonalId(
  pokemonWorkflow: PokemonWorkflow,
  currentSelectedPokemonPersonalId: number | null
) {
  const currentSelectedPokemonId =
    currentSelectedPokemonPersonalId === null ? null : Number(currentSelectedPokemonPersonalId);
  const currentSelection =
    currentSelectedPokemonId !== null && currentSelectedPokemonId !== 0
      ? pokemonWorkflow.pokemon.find(
          (pokemon) =>
            Number(pokemon.personalId) === currentSelectedPokemonId &&
            !isPlaceholderPokemonRecord(pokemon)
        )
      : null;
  return (
    currentSelection?.personalId ??
    pokemonWorkflow.pokemon.find((pokemon) => !isPlaceholderPokemonRecord(pokemon))?.personalId ??
    pokemonWorkflow.pokemon[0]?.personalId ??
    null
  );
}

function resolveSelectedEncounterTableId(
  encountersWorkflow: EncountersWorkflow,
  currentSelectedEncounterTableId: string | null
) {
  return encountersWorkflow.tables.some(
    (table) => table.tableId === currentSelectedEncounterTableId
  )
    ? currentSelectedEncounterTableId
    : (resolveDefaultEncounterTable(encountersWorkflow.tables)?.tableId ?? null);
}

function resolveDefaultEncounterTable(tables: EncounterTableRecord[]) {
  if (!tables.some(isPokemonLegendsZAEncounterTable)) {
    return tables[0] ?? null;
  }

  return [...tables].sort(compareEncounterTablesForDefaultSelection)[0] ?? null;
}

function compareEncounterTablesForDefaultSelection(
  left: EncounterTableRecord,
  right: EncounterTableRecord
) {
  const leftZa = isPokemonLegendsZAEncounterTable(left);
  const rightZa = isPokemonLegendsZAEncounterTable(right);
  if (leftZa && rightZa) {
    const locationSort =
      (left.locationSort ?? Number.MAX_SAFE_INTEGER) -
      (right.locationSort ?? Number.MAX_SAFE_INTEGER);
    if (locationSort !== 0) {
      return locationSort;
    }

    const labelSort =
      (parseTrailingInteger(left.tableLabel) ?? Number.MAX_SAFE_INTEGER) -
      (parseTrailingInteger(right.tableLabel) ?? Number.MAX_SAFE_INTEGER);
    if (labelSort !== 0) {
      return labelSort;
    }
  }

  return left.tableId.localeCompare(right.tableId);
}

function isPokemonLegendsZAEncounterTable(table: EncounterTableRecord) {
  return table.gameVersion === 'Pokemon Legends ZA' || table.locationKey != null;
}

function parseTrailingInteger(value: string | null | undefined) {
  const match = value?.match(/(\d+)$/);
  return match ? Number.parseInt(match[1]!, 10) : null;
}

function isPlaceholderPokemonRecord(pokemon: Pick<PokemonWorkflow['pokemon'][number], 'name' | 'personalId'>) {
  return Number(pokemon.personalId) === 0 || pokemon.name.trim().toLowerCase() === 'egg';
}

function createProjectSessionResetState(): Partial<WorkbenchState> {
  return {
    activeSection: 'health',
    applyResult: null,
    changePlan: null,
    editSession: null,
    editValidationDiagnostics: [],
    bagHookWorkflow: null,
    encounterSearchText: '',
    encountersWorkflow: null,
    catchCapWorkflow: null,
    hyperTrainingWorkflow: null,
    shinyRateWorkflow: null,
    typeChartWorkflow: null,
    fairyGymBoostsWorkflow: null,
    fashionUnlockWorkflow: null,
    gymUniformRemovalWorkflow: null, hyperspaceBypassWorkflow: null, ivScreenWorkflow: null,
    exeFsPatchSearchText: '',
    exeFsPatchWorkflow: null,
    flagworkSaveSearchText: '',
    flagworkSaveWorkflow: null,
    giftPokemonSearchText: '',
    giftPokemonWorkflow: null,
    tradePokemonSearchText: '',
    tradePokemonWorkflow: null,
    staticEncounterSearchText: '',
    staticEncountersWorkflow: null,
    rentalPokemonSearchText: '',
    rentalPokemonWorkflow: null,
    dynamaxAdventureSearchText: '',
    dynamaxAdventuresWorkflow: null,
    itemSearchText: '',
    itemsWorkflow: null,
    movesSearchText: '',
    movesWorkflow: null,
    openProject: null,
    behaviorSearchText: '',
    behaviorWorkflow: null,
    placementSearchText: '',
    placementWorkflow: null,
    pokemonSearchText: '',
    pokemonWorkflow: null,
    projectStatus: 'idle',
    teraRaidSearchText: '',
    teraRaidsWorkflow: null,
    raidBattleSearchText: '',
    raidBattlesWorkflow: null,
    raidRewardSearchText: '',
    raidRewardsWorkflow: null,
    raidBonusRewardSearchText: '',
    raidBonusRewardsWorkflow: null,
    royalCandySearchText: '',
    royalCandyWorkflow: null,
    startingItemsWorkflow: null, npcItemGiftWorkflow: null,
    spreadsheetImportPreview: null,
    spreadsheetImportSearchText: '',
    spreadsheetImportSourcePath: '',
    spreadsheetImportWorkflow: null,
    selectedEncounterTableId: null,
    selectedBagHookSlot: null,
    selectedExeFsCheckId: null,
    selectedExeFsPatchId: null,
    selectedCatchCapBadgeCount: null,
    selectedGiftPokemonIndex: null,
    selectedTradePokemonIndex: null,
    selectedStaticEncounterIndex: null,
    selectedRentalPokemonIndex: null,
    selectedDynamaxAdventureEntryIndex: null,
    selectedRoyalCandyCheckId: null,
    selectedRoyalCandyWorkflowId: null,
    selectedStartingItemSlot: null,
    selectedSpreadsheetImportProfileId: null,
    selectedFlagId: null,
    selectedItemId: null,
    selectedMoveId: null,
    selectedPokemonPersonalId: null,
    selectedBehaviorEntryId: null,
    selectedPlacementObjectId: null,
    selectedTeraRaidRecordId: null,
    selectedRaidBattleTableId: null,
    selectedRaidRewardTableId: null,
    selectedRaidBonusRewardTableId: null,
    selectedSaveBlockId: null,
    selectedShopId: null,
    selectedTextKey: null,
    selectedTrainerId: null,
    shopSearchText: '',
    shopsWorkflow: null,
    textSearchText: '',
    textWorkflow: null,
    trainerSearchText: '',
    trainersWorkflow: null,
    workflows: []
  };
}

export const useWorkbenchStore = create<WorkbenchState>((set) => ({
  activeSection: 'health',
  applyResult: null,
  changePlan: null,
  draftPaths: loadProjectPathDraft(),
  editSession: null,
  editValidationDiagnostics: [],
  bagHookWorkflow: null,
  encounterSearchText: '',
  encountersWorkflow: null,
  catchCapWorkflow: null,
  hyperTrainingWorkflow: null,
  shinyRateWorkflow: null,
  typeChartWorkflow: null,
  fairyGymBoostsWorkflow: null,
  fashionUnlockWorkflow: null,
  gymUniformRemovalWorkflow: null, hyperspaceBypassWorkflow: null, ivScreenWorkflow: null,
  exeFsPatchSearchText: '',
  exeFsPatchWorkflow: null,
  flagworkSaveSearchText: '',
  flagworkSaveWorkflow: null,
  giftPokemonSearchText: '',
  giftPokemonWorkflow: null,
  tradePokemonSearchText: '',
  tradePokemonWorkflow: null,
  staticEncounterSearchText: '',
  staticEncountersWorkflow: null,
  rentalPokemonSearchText: '',
  rentalPokemonWorkflow: null,
  dynamaxAdventureSearchText: '',
  dynamaxAdventuresWorkflow: null,
  itemSearchText: '',
  itemsWorkflow: null,
  movesSearchText: '',
  movesWorkflow: null,
  openProject: null,
  behaviorSearchText: '',
  behaviorWorkflow: null,
  placementSearchText: '',
  placementWorkflow: null,
  pokemonSearchText: '',
  pokemonWorkflow: null,
  projectStatus: 'idle',
  teraRaidSearchText: '',
  teraRaidsWorkflow: null,
  raidBattleSearchText: '',
  raidBattlesWorkflow: null,
  raidRewardSearchText: '',
  raidRewardsWorkflow: null,
  raidBonusRewardSearchText: '',
  raidBonusRewardsWorkflow: null,
  royalCandySearchText: '',
  royalCandyWorkflow: null,
  startingItemsWorkflow: null, npcItemGiftWorkflow: null,
  spreadsheetImportPreview: null,
  spreadsheetImportSearchText: '',
  spreadsheetImportSourcePath: '',
  spreadsheetImportWorkflow: null,
  selectedRaidRewardTableId: null,
  selectedBehaviorEntryId: null,
  selectedPlacementObjectId: null,
  selectedFlagId: null,
  selectedBagHookSlot: null,
  selectedExeFsCheckId: null,
  selectedExeFsPatchId: null,
  selectedCatchCapBadgeCount: null,
  selectedGiftPokemonIndex: null,
  selectedTradePokemonIndex: null,
  selectedStaticEncounterIndex: null,
  selectedRentalPokemonIndex: null,
  selectedDynamaxAdventureEntryIndex: null,
  selectedRoyalCandyCheckId: null,
  selectedRoyalCandyWorkflowId: null,
  selectedStartingItemSlot: null,
  selectedSpreadsheetImportProfileId: null,
  selectedItemId: null,
  selectedMoveId: null,
  selectedPokemonPersonalId: null,
  selectedSaveBlockId: null,
  selectedEncounterTableId: null,
  selectedTeraRaidRecordId: null,
  selectedRaidBattleTableId: null,
  selectedRaidBonusRewardTableId: null,
  selectedShopId: null,
  selectedTextKey: null,
  selectedTrainerId: null,
  shopSearchText: '',
  shopsWorkflow: null,
  textSearchText: '',
  textWorkflow: null,
  trainerSearchText: '',
  trainersWorkflow: null,
  workflows: [],
  setActiveSection: (activeSection) => set({ activeSection }),
  setApplyResult: (applyResult) => set({ applyResult }),
  setChangePlan: (changePlan) => set({ changePlan }),
  setDraftPath: (field, value) =>
    set((state) => {
      const draftPaths = {
        ...state.draftPaths,
        [field]: value
      };
      saveProjectPathDraft(draftPaths);

      return { draftPaths };
    }),
  clearSelectedGame: () =>
    set((state) => {
      const draftPaths = {
        ...state.draftPaths,
        selectedGame: null
      };
      saveProjectPathDraft(draftPaths);

      return {
        ...createProjectSessionResetState(),
        draftPaths
      };
    }),
  setEditSession: (editSession) => set({ applyResult: null, changePlan: null, editSession }),
  setEditValidationDiagnostics: (editValidationDiagnostics) => set({ editValidationDiagnostics }),
  setEncounterSearchText: (encounterSearchText) => set({ encounterSearchText }),
  setExeFsPatchSearchText: (exeFsPatchSearchText) => set({ exeFsPatchSearchText }),
  setFlagworkSaveSearchText: (flagworkSaveSearchText) => set({ flagworkSaveSearchText }),
  setGiftPokemonSearchText: (giftPokemonSearchText) => set({ giftPokemonSearchText }),
  setTradePokemonSearchText: (tradePokemonSearchText) => set({ tradePokemonSearchText }),
  setStaticEncounterSearchText: (staticEncounterSearchText) =>
    set({ staticEncounterSearchText }),
  setRentalPokemonSearchText: (rentalPokemonSearchText) => set({ rentalPokemonSearchText }),
  setDynamaxAdventureSearchText: (dynamaxAdventureSearchText) =>
    set({ dynamaxAdventureSearchText }),
  setBehaviorSearchText: (behaviorSearchText) => set({ behaviorSearchText }),
  setPlacementSearchText: (placementSearchText) => set({ placementSearchText }),
  setPokemonSearchText: (pokemonSearchText) => set({ pokemonSearchText }),
  setTeraRaidSearchText: (teraRaidSearchText) => set({ teraRaidSearchText }),
  setRaidBattleSearchText: (raidBattleSearchText) => set({ raidBattleSearchText }),
  setRaidRewardSearchText: (raidRewardSearchText) => set({ raidRewardSearchText }),
  setRaidBonusRewardSearchText: (raidBonusRewardSearchText) =>
    set({ raidBonusRewardSearchText }),
  setRoyalCandySearchText: (royalCandySearchText) => set({ royalCandySearchText }),
  setSpreadsheetImportSearchText: (spreadsheetImportSearchText) =>
    set({ spreadsheetImportSearchText }),
  setSpreadsheetImportSourcePath: (spreadsheetImportSourcePath) =>
    set({ spreadsheetImportSourcePath }),
  setSpreadsheetImportPreview: (spreadsheetImportPreview) =>
    set({ spreadsheetImportPreview }),
  setItemsWorkflow: (itemsWorkflow) =>
    set((state) => {
      const selectedItemId = itemsWorkflow.items.some(
        (item) => item.itemId === state.selectedItemId
      )
        ? state.selectedItemId
        : (itemsWorkflow.items[0]?.itemId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'items'),
        itemSearchText: '',
        itemsWorkflow,
        selectedItemId
      };
    }),
  setMovesWorkflow: (movesWorkflow) =>
    set((state) => {
      const selectedMoveId = movesWorkflow.moves.some(
        (move) => move.moveId === state.selectedMoveId
      )
        ? state.selectedMoveId
        : (movesWorkflow.moves[0]?.moveId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'moves'),
        movesSearchText: '',
        movesWorkflow,
        selectedMoveId
      };
    }),
  setPokemonWorkflow: (pokemonWorkflow) =>
    set((state) => {
      const selectedPokemonPersonalId = resolveSelectedPokemonPersonalId(
        pokemonWorkflow,
        state.selectedPokemonPersonalId
      );

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'pokemon'),
        pokemonSearchText: '',
        pokemonWorkflow,
        selectedPokemonPersonalId
      };
    }),
  setGiftPokemonWorkflow: (giftPokemonWorkflow) =>
    set((state) => {
      const selectedGiftPokemonIndex = giftPokemonWorkflow.gifts.some(
        (gift) => gift.giftIndex === state.selectedGiftPokemonIndex
      )
        ? state.selectedGiftPokemonIndex
        : (giftPokemonWorkflow.gifts[0]?.giftIndex ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'giftPokemon'),
        giftPokemonSearchText: '',
        giftPokemonWorkflow,
        selectedGiftPokemonIndex
      };
    }),
  setTradePokemonWorkflow: (tradePokemonWorkflow) =>
    set((state) => {
      const selectedTradePokemonIndex = tradePokemonWorkflow.trades.some(
        (trade) => trade.tradeIndex === state.selectedTradePokemonIndex
      )
        ? state.selectedTradePokemonIndex
        : (tradePokemonWorkflow.trades[0]?.tradeIndex ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'tradePokemon'),
        selectedTradePokemonIndex,
        tradePokemonSearchText: '',
        tradePokemonWorkflow
      };
    }),
  setStaticEncountersWorkflow: (staticEncountersWorkflow) =>
    set((state) => {
      const selectedStaticEncounterIndex = staticEncountersWorkflow.encounters.some(
        (encounter) => encounter.encounterIndex === state.selectedStaticEncounterIndex
      )
        ? state.selectedStaticEncounterIndex
        : (staticEncountersWorkflow.encounters[0]?.encounterIndex ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'staticEncounters'),
        selectedStaticEncounterIndex,
        staticEncounterSearchText: '',
        staticEncountersWorkflow
      };
    }),
  setRentalPokemonWorkflow: (rentalPokemonWorkflow) =>
    set((state) => {
      const selectedRentalPokemonIndex = rentalPokemonWorkflow.rentals.some(
        (rental) => rental.rentalIndex === state.selectedRentalPokemonIndex
      )
        ? state.selectedRentalPokemonIndex
        : (rentalPokemonWorkflow.rentals[0]?.rentalIndex ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'rentalPokemon'),
        rentalPokemonSearchText: '',
        rentalPokemonWorkflow,
        selectedRentalPokemonIndex
      };
    }),
  setDynamaxAdventuresWorkflow: (dynamaxAdventuresWorkflow) =>
    set((state) => {
      const selectedDynamaxAdventureEntryIndex = dynamaxAdventuresWorkflow.encounters.some(
        (encounter) => encounter.entryIndex === state.selectedDynamaxAdventureEntryIndex
      )
        ? state.selectedDynamaxAdventureEntryIndex
        : (dynamaxAdventuresWorkflow.encounters[0]?.entryIndex ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'dynamaxAdventures'),
        dynamaxAdventureSearchText: '',
        dynamaxAdventuresWorkflow,
        selectedDynamaxAdventureEntryIndex
      };
    }),
  setItemSearchText: (itemSearchText) => set({ itemSearchText }),
  setMovesSearchText: (movesSearchText) => set({ movesSearchText }),
  setShopSearchText: (shopSearchText) => set({ shopSearchText }),
  setTextSearchText: (textSearchText) => set({ textSearchText }),
  setTrainerSearchText: (trainerSearchText) => set({ trainerSearchText }),
  setOpenProject: (openProject) =>
    set({
      applyResult: null,
      changePlan: null,
      editSession: null,
      editValidationDiagnostics: [],
      bagHookWorkflow: null,
      encounterSearchText: '',
      encountersWorkflow: null,
      catchCapWorkflow: null,
      hyperTrainingWorkflow: null,
      shinyRateWorkflow: null,
      typeChartWorkflow: null,
      fairyGymBoostsWorkflow: null,
      fashionUnlockWorkflow: null,
      gymUniformRemovalWorkflow: null,
      ivScreenWorkflow: null,
      exeFsPatchSearchText: '',
      exeFsPatchWorkflow: null,
      flagworkSaveSearchText: '',
      flagworkSaveWorkflow: null,
      giftPokemonSearchText: '',
      giftPokemonWorkflow: null,
      tradePokemonSearchText: '',
      tradePokemonWorkflow: null,
      staticEncounterSearchText: '',
      staticEncountersWorkflow: null,
      rentalPokemonSearchText: '',
      rentalPokemonWorkflow: null,
      dynamaxAdventureSearchText: '',
      dynamaxAdventuresWorkflow: null,
      itemSearchText: '',
      itemsWorkflow: null,
      movesSearchText: '',
      movesWorkflow: null,
      openProject,
      behaviorSearchText: '',
      behaviorWorkflow: null,
      placementSearchText: '',
      placementWorkflow: null,
      pokemonSearchText: '',
      pokemonWorkflow: null,
      projectStatus: 'open',
      raidRewardSearchText: '',
      raidRewardsWorkflow: null,
      raidBonusRewardSearchText: '',
      raidBonusRewardsWorkflow: null,
      royalCandySearchText: '',
      royalCandyWorkflow: null,
      startingItemsWorkflow: null, npcItemGiftWorkflow: null,
      spreadsheetImportPreview: null,
      spreadsheetImportSearchText: '',
      spreadsheetImportSourcePath: '',
      spreadsheetImportWorkflow: null,
      selectedEncounterTableId: null,
      selectedBagHookSlot: null,
      selectedExeFsCheckId: null,
      selectedExeFsPatchId: null,
      selectedCatchCapBadgeCount: null,
      selectedGiftPokemonIndex: null,
      selectedTradePokemonIndex: null,
      selectedStaticEncounterIndex: null,
      selectedRentalPokemonIndex: null,
      selectedDynamaxAdventureEntryIndex: null,
      selectedRoyalCandyCheckId: null,
      selectedRoyalCandyWorkflowId: null,
      selectedStartingItemSlot: null,
      selectedSpreadsheetImportProfileId: null,
      selectedFlagId: null,
      selectedItemId: null,
      selectedMoveId: null,
      selectedPokemonPersonalId: null,
      selectedBehaviorEntryId: null,
      selectedPlacementObjectId: null,
      selectedRaidRewardTableId: null,
      selectedRaidBonusRewardTableId: null,
      selectedSaveBlockId: null,
      selectedShopId: null,
      selectedTextKey: null,
      selectedTrainerId: null,
      shopSearchText: '',
      shopsWorkflow: null,
      textSearchText: '',
      textWorkflow: null,
      trainerSearchText: '',
      trainersWorkflow: null
    }),
  setProjectHealth: (health) =>
    set((state) => ({
      openProject: state.openProject
        ? {
            ...state.openProject,
            health
          }
        : {
            fileGraph: {
              entries: [],
              summary: health.fileGraph
            },
            health,
            projectId: 'pending-project'
          },
      projectStatus: 'idle'
    })),
  setProjectStatus: (projectStatus) => set({ projectStatus }),
  setSelectedTeraRaidRecordId: (selectedTeraRaidRecordId) =>
    set({ selectedTeraRaidRecordId }),
  setSelectedRaidBattleTableId: (selectedRaidBattleTableId) =>
    set({ selectedRaidBattleTableId }),
  setSelectedRaidRewardTableId: (selectedRaidRewardTableId) =>
    set({ selectedRaidRewardTableId }),
  setSelectedRaidBonusRewardTableId: (selectedRaidBonusRewardTableId) =>
    set({ selectedRaidBonusRewardTableId }),
  setSelectedBehaviorEntryId: (selectedBehaviorEntryId) =>
    set({ selectedBehaviorEntryId }),
  setSelectedPlacementObjectId: (selectedPlacementObjectId) =>
    set({ selectedPlacementObjectId }),
  setSelectedFlagId: (selectedFlagId) => set({ selectedFlagId }),
  setSelectedSaveBlockId: (selectedSaveBlockId) => set({ selectedSaveBlockId }),
  setSelectedBagHookSlot: (selectedBagHookSlot) => set({ selectedBagHookSlot }),
  setSelectedExeFsCheckId: (selectedExeFsCheckId) => set({ selectedExeFsCheckId }),
  setSelectedExeFsPatchId: (selectedExeFsPatchId) => set({ selectedExeFsPatchId }),
  setSelectedCatchCapBadgeCount: (selectedCatchCapBadgeCount) =>
    set({ selectedCatchCapBadgeCount }),
  setSelectedGiftPokemonIndex: (selectedGiftPokemonIndex) =>
    set({ selectedGiftPokemonIndex }),
  setSelectedTradePokemonIndex: (selectedTradePokemonIndex) =>
    set({ selectedTradePokemonIndex }),
  setSelectedStaticEncounterIndex: (selectedStaticEncounterIndex) =>
    set({ selectedStaticEncounterIndex }),
  setSelectedRentalPokemonIndex: (selectedRentalPokemonIndex) =>
    set({ selectedRentalPokemonIndex }),
  setSelectedDynamaxAdventureEntryIndex: (selectedDynamaxAdventureEntryIndex) =>
    set({ selectedDynamaxAdventureEntryIndex }),
  setSelectedRoyalCandyCheckId: (selectedRoyalCandyCheckId) =>
    set({ selectedRoyalCandyCheckId }),
  setSelectedRoyalCandyWorkflowId: (selectedRoyalCandyWorkflowId) =>
    set({ selectedRoyalCandyWorkflowId }),
  setSelectedStartingItemSlot: (selectedStartingItemSlot) =>
    set({ selectedStartingItemSlot }),
  setSelectedSpreadsheetImportProfileId: (selectedSpreadsheetImportProfileId) =>
    set({ selectedSpreadsheetImportProfileId }),
  setSelectedEncounterTableId: (selectedEncounterTableId) => set({ selectedEncounterTableId }),
  setSelectedItemId: (selectedItemId) => set({ selectedItemId }),
  setSelectedMoveId: (selectedMoveId) => set({ selectedMoveId }),
  setSelectedPokemonPersonalId: (selectedPokemonPersonalId) =>
    set({ selectedPokemonPersonalId }),
  setSelectedShopId: (selectedShopId) => set({ selectedShopId }),
  setSelectedTextKey: (selectedTextKey) => set({ selectedTextKey }),
  setSelectedTrainerId: (selectedTrainerId) => set({ selectedTrainerId }),
  setTextWorkflow: (textWorkflow) =>
    set((state) => {
      const selectedTextKey = textWorkflow.entries.some(
        (entry) => entry.textKey === state.selectedTextKey
      )
        ? state.selectedTextKey
        : (textWorkflow.entries[0]?.textKey ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'text'),
        selectedTextKey,
        textSearchText: '',
        textWorkflow
      };
    }),
  setTrainersWorkflow: (trainersWorkflow) =>
    set((state) => {
      const selectedTrainerId = trainersWorkflow.trainers.some(
        (trainer) => trainer.trainerId === state.selectedTrainerId
      )
        ? state.selectedTrainerId
        : (trainersWorkflow.trainers[0]?.trainerId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'trainers'),
        selectedTrainerId,
        trainerSearchText: '',
        trainersWorkflow
      };
    }),
  setShopsWorkflow: (shopsWorkflow) =>
    set((state) => {
      const selectedShopId = shopsWorkflow.shops.some(
        (shop) => shop.shopId === state.selectedShopId
      )
        ? state.selectedShopId
        : (shopsWorkflow.shops[0]?.shopId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'shops'),
        selectedShopId,
        shopSearchText: '',
        shopsWorkflow
      };
    }),
  setEncountersWorkflow: (encountersWorkflow) =>
    set((state) => {
      const selectedEncounterTableId = resolveSelectedEncounterTableId(
        encountersWorkflow,
        state.selectedEncounterTableId
      );

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'encounters'),
        encounterSearchText: '',
        encountersWorkflow,
        selectedEncounterTableId
      };
    }),
  setTeraRaidsWorkflow: (teraRaidsWorkflow) =>
    set((state) => {
      const selectedTeraRaidRecordId = teraRaidsWorkflow.raids.some(
        (raid) => raid.recordId === state.selectedTeraRaidRecordId
      )
        ? state.selectedTeraRaidRecordId
        : (teraRaidsWorkflow.raids[0]?.recordId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'teraRaids'),
        selectedTeraRaidRecordId,
        teraRaidSearchText: '',
        teraRaidsWorkflow
      };
    }),
  setRaidBattlesWorkflow: (raidBattlesWorkflow) =>
    set((state) => {
      const selectedRaidBattleTableId = raidBattlesWorkflow.tables.some(
        (table) => table.tableId === state.selectedRaidBattleTableId
      )
        ? state.selectedRaidBattleTableId
        : (raidBattlesWorkflow.tables[0]?.tableId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'raidBattles'),
        raidBattleSearchText: '',
        raidBattlesWorkflow,
        selectedRaidBattleTableId
      };
    }),
  setRaidRewardsWorkflow: (raidRewardsWorkflow) =>
    set((state) => {
      const selectedRaidRewardTableId = raidRewardsWorkflow.tables.some(
        (table) => table.tableId === state.selectedRaidRewardTableId
      )
        ? state.selectedRaidRewardTableId
        : (raidRewardsWorkflow.tables[0]?.tableId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'raidRewards'),
        raidRewardSearchText: '',
        raidRewardsWorkflow,
        selectedRaidRewardTableId
      };
    }),
  setRaidBonusRewardsWorkflow: (raidBonusRewardsWorkflow) =>
    set((state) => {
      const selectedRaidBonusRewardTableId = raidBonusRewardsWorkflow.tables.some(
        (table) => table.tableId === state.selectedRaidBonusRewardTableId
      )
        ? state.selectedRaidBonusRewardTableId
        : (raidBonusRewardsWorkflow.tables[0]?.tableId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'raidBonusRewards'),
        raidBonusRewardSearchText: '',
        raidBonusRewardsWorkflow,
        selectedRaidBonusRewardTableId
      };
    }),
  setPlacementWorkflow: (placementWorkflow) =>
    set((state) => {
      const selectedPlacementObjectId = placementWorkflow.objects.some(
        (placedObject) => placedObject.objectId === state.selectedPlacementObjectId
      )
        ? state.selectedPlacementObjectId
        : (placementWorkflow.objects[0]?.objectId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'placement'),
        placementSearchText: '',
        placementWorkflow,
        selectedPlacementObjectId
      };
    }),
  setBehaviorWorkflow: (behaviorWorkflow) =>
    set((state) => {
      const selectedBehaviorEntryId = behaviorWorkflow.entries.some(
        (entry) => entry.entryId === state.selectedBehaviorEntryId
      )
        ? state.selectedBehaviorEntryId
        : (behaviorWorkflow.entries[0]?.entryId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'behavior'),
        behaviorSearchText: '',
        behaviorWorkflow,
        selectedBehaviorEntryId
      };
    }),
  setFlagworkSaveWorkflow: (flagworkSaveWorkflow) =>
    set((state) => {
      const selectedFlagId = flagworkSaveWorkflow.flags.some(
        (flag) => flag.flagId === state.selectedFlagId
      )
        ? state.selectedFlagId
        : (flagworkSaveWorkflow.flags[0]?.flagId ?? null);
      const selectedSaveBlockId = flagworkSaveWorkflow.saveBlocks.some(
        (saveBlock) => saveBlock.blockId === state.selectedSaveBlockId
      )
        ? state.selectedSaveBlockId
        : (flagworkSaveWorkflow.saveBlocks[0]?.blockId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'flagworkSave'),
        flagworkSaveSearchText: '',
        flagworkSaveWorkflow,
        selectedFlagId,
        selectedSaveBlockId
      };
    }),
  setBagHookWorkflow: (bagHookWorkflow) =>
    set((state) => {
      const selectedBagHookSlot = bagHookWorkflow.slots.some(
        (slot) => slot.slot === state.selectedBagHookSlot
      )
        ? state.selectedBagHookSlot
        : (bagHookWorkflow.slots[0]?.slot ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'bagHook'),
        bagHookWorkflow,
        selectedBagHookSlot
      };
    }),
  setCatchCapWorkflow: (catchCapWorkflow) =>
    set((state) => {
      const selectedCatchCapBadgeCount = catchCapWorkflow.caps.some(
        (cap) => cap.badgeCount === state.selectedCatchCapBadgeCount
      )
        ? state.selectedCatchCapBadgeCount
        : (catchCapWorkflow.caps[0]?.badgeCount ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'catchCap'),
        catchCapWorkflow,
        selectedCatchCapBadgeCount
      };
    }),
  setHyperTrainingWorkflow: (hyperTrainingWorkflow) =>
    set((state) => ({
      activeSection: resolveWorkflowLoadSection(state.activeSection, 'hyperTraining'),
      hyperTrainingWorkflow
    })),
  setShinyRateWorkflow: (shinyRateWorkflow) =>
    set((state) => ({
      activeSection: resolveWorkflowLoadSection(state.activeSection, 'shinyRate'),
      shinyRateWorkflow
    })),
  setTypeChartWorkflow: (typeChartWorkflow) =>
    set((state) => ({
      activeSection: resolveWorkflowLoadSection(state.activeSection, 'typeChart'),
      typeChartWorkflow
    })),
  setFairyGymBoostsWorkflow: (fairyGymBoostsWorkflow) =>
    set((state) => ({
      activeSection: resolveWorkflowLoadSection(state.activeSection, 'fairyGymBoosts'),
      fairyGymBoostsWorkflow
    })),
  setFashionUnlockWorkflow: (fashionUnlockWorkflow) =>
    set((state) => ({
      activeSection: resolveWorkflowLoadSection(state.activeSection, 'fashionUnlock'),
      fashionUnlockWorkflow
    })),
  setIvScreenWorkflow: (ivScreenWorkflow) =>
    set((state) => ({
      activeSection: resolveWorkflowLoadSection(state.activeSection, 'ivScreen'),
      ivScreenWorkflow
    })),
  setGymUniformRemovalWorkflow: (gymUniformRemovalWorkflow) =>
    set((state) => ({
      activeSection: resolveWorkflowLoadSection(state.activeSection, 'gymUniformRemoval'),
      gymUniformRemovalWorkflow
    })),
  setHyperspaceBypassWorkflow: (hyperspaceBypassWorkflow) => set((state) => ({ activeSection: resolveWorkflowLoadSection(state.activeSection, 'hyperspaceBypass'), hyperspaceBypassWorkflow })),
  setExeFsPatchWorkflow: (exeFsPatchWorkflow) =>
    set((state) => {
      const selectedExeFsPatchId = exeFsPatchWorkflow.patches.some(
        (patch) => patch.patchId === state.selectedExeFsPatchId
      )
        ? state.selectedExeFsPatchId
        : (exeFsPatchWorkflow.patches[0]?.patchId ?? null);
      const selectedExeFsCheckId = exeFsPatchWorkflow.checks.some(
        (check) => check.checkId === state.selectedExeFsCheckId
      )
        ? state.selectedExeFsCheckId
        : (exeFsPatchWorkflow.checks[0]?.checkId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'exefsPatches'),
        exeFsPatchSearchText: '',
        exeFsPatchWorkflow,
        selectedExeFsCheckId,
        selectedExeFsPatchId
      };
    }),
  setRoyalCandyWorkflow: (royalCandyWorkflow) =>
    set((state) => {
      const selectedRoyalCandyWorkflowId = royalCandyWorkflow.workflows.some(
        (workflow) => workflow.workflowId === state.selectedRoyalCandyWorkflowId
      )
        ? state.selectedRoyalCandyWorkflowId
        : (royalCandyWorkflow.workflows[0]?.workflowId ?? null);
      const selectedRoyalCandyCheckId = royalCandyWorkflow.checks.some(
        (check) => check.checkId === state.selectedRoyalCandyCheckId
      )
        ? state.selectedRoyalCandyCheckId
        : (royalCandyWorkflow.checks[0]?.checkId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'royalCandy'),
        royalCandySearchText: '',
        royalCandyWorkflow,
        selectedRoyalCandyCheckId,
        selectedRoyalCandyWorkflowId
      };
    }),
  setStartingItemsWorkflow: (startingItemsWorkflow) =>
    set((state) => {
      const selectedStartingItemSlot = startingItemsWorkflow.grants.some(
        (grant) => grant.slot === state.selectedStartingItemSlot
      )
        ? state.selectedStartingItemSlot
        : (startingItemsWorkflow.grants[0]?.slot ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'startingItems'),
        selectedStartingItemSlot,
        startingItemsWorkflow
      };
    }),
  setNpcItemGiftWorkflow: (npcItemGiftWorkflow) =>
    set((state) => ({ activeSection: resolveWorkflowLoadSection(state.activeSection, 'npcItemGift'), npcItemGiftWorkflow })),
  setSpreadsheetImportWorkflow: (spreadsheetImportWorkflow) =>
    set((state) => {
      const selectedSpreadsheetImportProfileId = spreadsheetImportWorkflow.profiles.some(
        (profile) => profile.profileId === state.selectedSpreadsheetImportProfileId
      )
        ? state.selectedSpreadsheetImportProfileId
        : (spreadsheetImportWorkflow.profiles[0]?.profileId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'spreadsheetImport'),
        selectedSpreadsheetImportProfileId,
        spreadsheetImportPreview: null,
        spreadsheetImportSearchText: '',
        spreadsheetImportWorkflow
      };
    }),
  setSelectedGame: (selectedGame) =>
    set(() => {
      const cachedPaths = loadValidatedProjectPathDraft(selectedGame);
      const draftPaths = {
        ...(cachedPaths ?? createEmptyProjectPathDraftValues()),
        selectedGame
      };
      saveProjectPathDraft(draftPaths);

      return {
        ...createProjectSessionResetState(),
        draftPaths
      };
    }),
  rememberValidatedProjectPaths: (draftPaths) => {
    saveValidatedProjectPathDraft(draftPaths);
    saveProjectPathDraft(draftPaths);
  },
  setWorkflows: (workflows) => set({ workflows })
}));

function loadProjectPathDraft(): ProjectPathDraft {
  const emptyDraft = createEmptyProjectPathDraft();

  if (typeof window === 'undefined') {
    return emptyDraft;
  }

  try {
    const storedValue = window.localStorage.getItem(projectPathDraftStorageKey);

    if (!storedValue) {
      return emptyDraft;
    }

    const parsedValue = JSON.parse(storedValue) as Partial<ProjectPathDraft>;

    return {
      baseExeFsPath: typeof parsedValue.baseExeFsPath === 'string' ? parsedValue.baseExeFsPath : '',
      baseRomFsPath:
        typeof parsedValue.baseRomFsPath === 'string' ? parsedValue.baseRomFsPath : '',
      outputRootPath:
        typeof parsedValue.outputRootPath === 'string' ? parsedValue.outputRootPath : '',
      pokemonLegendsZASupportFolderPath: typeof parsedValue.pokemonLegendsZASupportFolderPath === 'string' ? parsedValue.pokemonLegendsZASupportFolderPath : '',
      saveFilePath: typeof parsedValue.saveFilePath === 'string' ? parsedValue.saveFilePath : '',
      scarletVioletSupportFolderPath: typeof parsedValue.scarletVioletSupportFolderPath === 'string' ? parsedValue.scarletVioletSupportFolderPath : '',
      selectedGame: null
    };
  } catch {
    return emptyDraft;
  }
}

function loadValidatedProjectPathDraft(selectedGame: ProjectGame): ProjectPathDraftValues | null {
  const cache = loadValidatedProjectPathCache();
  return cache[selectedGame] ?? null;
}

function loadValidatedProjectPathCache(): ValidatedProjectPathCache {
  if (typeof window === 'undefined') {
    return {};
  }

  try {
    const storedValue = window.localStorage.getItem(validatedProjectPathCacheStorageKey);

    if (!storedValue) {
      return {};
    }

    const parsedValue = JSON.parse(storedValue) as Partial<
      Record<ProjectGame, Partial<ProjectPathDraftValues>>
    >;

    const cache: ValidatedProjectPathCache = {};
    const shieldPaths = coerceProjectPathDraftValues(parsedValue.shield);
    const swordPaths = coerceProjectPathDraftValues(parsedValue.sword);
    const scarletPaths = coerceProjectPathDraftValues(parsedValue.scarlet);
    const violetPaths = coerceProjectPathDraftValues(parsedValue.violet);
    const zaPaths = coerceProjectPathDraftValues(parsedValue.za);

    if (shieldPaths) {
      cache.shield = shieldPaths;
    }

    if (swordPaths) {
      cache.sword = swordPaths;
    }

    if (scarletPaths) {
      cache.scarlet = scarletPaths;
    }

    if (violetPaths) {
      cache.violet = violetPaths;
    }

    if (zaPaths) {
      cache.za = zaPaths;
    }

    return cache;
  } catch {
    return {};
  }
}

function saveProjectPathDraft(draftPaths: ProjectPathDraft) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    const persistentDraftPaths = {
      baseExeFsPath: draftPaths.baseExeFsPath, baseRomFsPath: draftPaths.baseRomFsPath, outputRootPath: draftPaths.outputRootPath, pokemonLegendsZASupportFolderPath: draftPaths.pokemonLegendsZASupportFolderPath, saveFilePath: draftPaths.saveFilePath, scarletVioletSupportFolderPath: draftPaths.scarletVioletSupportFolderPath
    };
    window.localStorage.setItem(projectPathDraftStorageKey, JSON.stringify(persistentDraftPaths));
  } catch {
    // Storage can be unavailable in hardened browser contexts; typed paths should still work.
  }
}

function saveValidatedProjectPathDraft(draftPaths: ProjectPathDraft) {
  if (typeof window === 'undefined' || draftPaths.selectedGame === null) {
    return;
  }

  try {
    const cache = loadValidatedProjectPathCache();
    cache[draftPaths.selectedGame] = toProjectPathDraftValues(draftPaths);
    window.localStorage.setItem(
      validatedProjectPathCacheStorageKey,
      JSON.stringify(cache)
    );
  } catch {
    // Storage can be unavailable in hardened browser contexts; validation should still work.
  }
}

function createEmptyProjectPathDraft(): ProjectPathDraft {
  return {
    ...createEmptyProjectPathDraftValues(),
    selectedGame: null
  };
}

function createEmptyProjectPathDraftValues(): ProjectPathDraftValues {
  return {
    baseExeFsPath: '',
    baseRomFsPath: '',
    outputRootPath: '',
    pokemonLegendsZASupportFolderPath: '',
    saveFilePath: '',
    scarletVioletSupportFolderPath: ''
  };
}

function coerceProjectPathDraftValues(
  draftPaths: Partial<ProjectPathDraftValues> | null | undefined
): ProjectPathDraftValues | undefined {
  if (!draftPaths) {
    return undefined;
  }

  return {
    baseExeFsPath: typeof draftPaths.baseExeFsPath === 'string' ? draftPaths.baseExeFsPath : '',
    baseRomFsPath: typeof draftPaths.baseRomFsPath === 'string' ? draftPaths.baseRomFsPath : '',
    outputRootPath:
      typeof draftPaths.outputRootPath === 'string' ? draftPaths.outputRootPath : '',
    pokemonLegendsZASupportFolderPath: typeof draftPaths.pokemonLegendsZASupportFolderPath === 'string' ? draftPaths.pokemonLegendsZASupportFolderPath : '',
    saveFilePath: typeof draftPaths.saveFilePath === 'string' ? draftPaths.saveFilePath : '',
    scarletVioletSupportFolderPath: typeof draftPaths.scarletVioletSupportFolderPath === 'string' ? draftPaths.scarletVioletSupportFolderPath : ''
  };
}

function toProjectPathDraftValues(draftPaths: ProjectPathDraft): ProjectPathDraftValues {
  return {
    baseExeFsPath: draftPaths.baseExeFsPath,
    baseRomFsPath: draftPaths.baseRomFsPath,
    outputRootPath: draftPaths.outputRootPath,
    pokemonLegendsZASupportFolderPath: draftPaths.pokemonLegendsZASupportFolderPath,
    saveFilePath: draftPaths.saveFilePath,
    scarletVioletSupportFolderPath: draftPaths.scarletVioletSupportFolderPath
  };
}
