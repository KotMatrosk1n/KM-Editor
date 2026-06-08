/* SPDX-License-Identifier: GPL-3.0-only */

import { create } from 'zustand';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type ChangePlan,
  type EditSession,
  type EncountersWorkflow,
  type ExeFsPatchWorkflow,
  type FlagworkSaveWorkflow,
  type GiftPokemonWorkflow,
  type ItemsWorkflow,
  type MovesWorkflow,
  type PlacementWorkflow,
  type PokemonWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
  type RaidRewardsWorkflow,
  type RoyalCandyWorkflow,
  type SpreadsheetImportPreview,
  type SpreadsheetImportWorkflow,
  type ShopsWorkflow,
  type TextWorkflow,
  type TrainersWorkflow,
  type WorkflowSummary
} from './bridge/contracts';

export type WorkbenchSection =
  | 'health'
  | 'workflows'
  | 'items'
  | 'pokemon'
  | 'moves'
  | 'text'
  | 'trainers'
  | 'giftPokemon'
  | 'shops'
  | 'encounters'
  | 'raidRewards'
  | 'placement'
  | 'flagworkSave'
  | 'exefsPatches'
  | 'royalCandy'
  | 'spreadsheetImport'
  | 'changes';

export type ProjectPathDraft = {
  baseExeFsPath: string;
  baseRomFsPath: string;
  outputRootPath: string;
  saveFilePath: string;
};

const projectPathDraftStorageKey = 'km-editor.project-path-draft.v1';

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
  encounterSearchText: string;
  encountersWorkflow: EncountersWorkflow | null;
  exeFsPatchSearchText: string;
  exeFsPatchWorkflow: ExeFsPatchWorkflow | null;
  flagworkSaveSearchText: string;
  flagworkSaveWorkflow: FlagworkSaveWorkflow | null;
  giftPokemonSearchText: string;
  giftPokemonWorkflow: GiftPokemonWorkflow | null;
  itemSearchText: string;
  itemsWorkflow: ItemsWorkflow | null;
  movesSearchText: string;
  movesWorkflow: MovesWorkflow | null;
  openProject: OpenProjectState | null;
  placementSearchText: string;
  placementWorkflow: PlacementWorkflow | null;
  pokemonSearchText: string;
  pokemonWorkflow: PokemonWorkflow | null;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
  raidRewardSearchText: string;
  raidRewardsWorkflow: RaidRewardsWorkflow | null;
  royalCandySearchText: string;
  royalCandyWorkflow: RoyalCandyWorkflow | null;
  spreadsheetImportPreview: SpreadsheetImportPreview | null;
  spreadsheetImportSearchText: string;
  spreadsheetImportSourcePath: string;
  spreadsheetImportWorkflow: SpreadsheetImportWorkflow | null;
  selectedRaidRewardTableId: string | null;
  selectedPlacementObjectId: string | null;
  selectedFlagId: string | null;
  selectedSaveBlockId: string | null;
  selectedExeFsCheckId: string | null;
  selectedExeFsPatchId: string | null;
  selectedGiftPokemonIndex: number | null;
  selectedRoyalCandyCheckId: string | null;
  selectedRoyalCandyWorkflowId: string | null;
  selectedSpreadsheetImportProfileId: string | null;
  selectedItemId: number | null;
  selectedMoveId: number | null;
  selectedPokemonPersonalId: number | null;
  selectedEncounterTableId: string | null;
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
  setDraftPath: (field: keyof ProjectPathDraft, value: string) => void;
  setActiveSection: (activeSection: WorkbenchSection) => void;
  setApplyResult: (applyResult: ApplyResult | null) => void;
  setChangePlan: (changePlan: ChangePlan | null) => void;
  setEditSession: (editSession: EditSession | null) => void;
  setEditValidationDiagnostics: (diagnostics: ApiDiagnostic[]) => void;
  setEncounterSearchText: (encounterSearchText: string) => void;
  setEncountersWorkflow: (encountersWorkflow: EncountersWorkflow) => void;
  setExeFsPatchSearchText: (exeFsPatchSearchText: string) => void;
  setExeFsPatchWorkflow: (exeFsPatchWorkflow: ExeFsPatchWorkflow) => void;
  setFlagworkSaveSearchText: (flagworkSaveSearchText: string) => void;
  setFlagworkSaveWorkflow: (flagworkSaveWorkflow: FlagworkSaveWorkflow) => void;
  setGiftPokemonSearchText: (giftPokemonSearchText: string) => void;
  setGiftPokemonWorkflow: (giftPokemonWorkflow: GiftPokemonWorkflow) => void;
  setItemsWorkflow: (itemsWorkflow: ItemsWorkflow) => void;
  setItemSearchText: (itemSearchText: string) => void;
  setMovesSearchText: (movesSearchText: string) => void;
  setMovesWorkflow: (movesWorkflow: MovesWorkflow) => void;
  setOpenProject: (project: OpenProjectState) => void;
  setPlacementSearchText: (placementSearchText: string) => void;
  setPlacementWorkflow: (placementWorkflow: PlacementWorkflow) => void;
  setPokemonSearchText: (pokemonSearchText: string) => void;
  setPokemonWorkflow: (pokemonWorkflow: PokemonWorkflow) => void;
  setProjectHealth: (health: ProjectHealth) => void;
  setProjectStatus: (projectStatus: WorkbenchState['projectStatus']) => void;
  setRaidRewardSearchText: (raidRewardSearchText: string) => void;
  setRaidRewardsWorkflow: (raidRewardsWorkflow: RaidRewardsWorkflow) => void;
  setRoyalCandySearchText: (royalCandySearchText: string) => void;
  setRoyalCandyWorkflow: (royalCandyWorkflow: RoyalCandyWorkflow) => void;
  setSpreadsheetImportPreview: (preview: SpreadsheetImportPreview | null) => void;
  setSpreadsheetImportSearchText: (spreadsheetImportSearchText: string) => void;
  setSpreadsheetImportSourcePath: (sourcePath: string) => void;
  setSpreadsheetImportWorkflow: (spreadsheetImportWorkflow: SpreadsheetImportWorkflow) => void;
  setSelectedRaidRewardTableId: (selectedRaidRewardTableId: string | null) => void;
  setSelectedPlacementObjectId: (selectedPlacementObjectId: string | null) => void;
  setSelectedFlagId: (selectedFlagId: string | null) => void;
  setSelectedSaveBlockId: (selectedSaveBlockId: string | null) => void;
  setSelectedExeFsCheckId: (selectedExeFsCheckId: string | null) => void;
  setSelectedExeFsPatchId: (selectedExeFsPatchId: string | null) => void;
  setSelectedGiftPokemonIndex: (selectedGiftPokemonIndex: number | null) => void;
  setSelectedRoyalCandyCheckId: (selectedRoyalCandyCheckId: string | null) => void;
  setSelectedRoyalCandyWorkflowId: (selectedRoyalCandyWorkflowId: string | null) => void;
  setSelectedSpreadsheetImportProfileId: (
    selectedSpreadsheetImportProfileId: string | null
  ) => void;
  setSelectedItemId: (selectedItemId: number | null) => void;
  setSelectedMoveId: (selectedMoveId: number | null) => void;
  setSelectedPokemonPersonalId: (selectedPokemonPersonalId: number | null) => void;
  setSelectedEncounterTableId: (selectedEncounterTableId: string | null) => void;
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
};

function resolveWorkflowLoadSection(
  activeSection: WorkbenchSection,
  workflowSection: WorkbenchSection
) {
  return activeSection === 'workflows' ? workflowSection : activeSection;
}

export const useWorkbenchStore = create<WorkbenchState>((set) => ({
  activeSection: 'health',
  applyResult: null,
  changePlan: null,
  draftPaths: loadProjectPathDraft(),
  editSession: null,
  editValidationDiagnostics: [],
  encounterSearchText: '',
  encountersWorkflow: null,
  exeFsPatchSearchText: '',
  exeFsPatchWorkflow: null,
  flagworkSaveSearchText: '',
  flagworkSaveWorkflow: null,
  giftPokemonSearchText: '',
  giftPokemonWorkflow: null,
  itemSearchText: '',
  itemsWorkflow: null,
  movesSearchText: '',
  movesWorkflow: null,
  openProject: null,
  placementSearchText: '',
  placementWorkflow: null,
  pokemonSearchText: '',
  pokemonWorkflow: null,
  projectStatus: 'idle',
  raidRewardSearchText: '',
  raidRewardsWorkflow: null,
  royalCandySearchText: '',
  royalCandyWorkflow: null,
  spreadsheetImportPreview: null,
  spreadsheetImportSearchText: '',
  spreadsheetImportSourcePath: '',
  spreadsheetImportWorkflow: null,
  selectedRaidRewardTableId: null,
  selectedPlacementObjectId: null,
  selectedFlagId: null,
  selectedExeFsCheckId: null,
  selectedExeFsPatchId: null,
  selectedGiftPokemonIndex: null,
  selectedRoyalCandyCheckId: null,
  selectedRoyalCandyWorkflowId: null,
  selectedSpreadsheetImportProfileId: null,
  selectedItemId: null,
  selectedMoveId: null,
  selectedPokemonPersonalId: null,
  selectedSaveBlockId: null,
  selectedEncounterTableId: null,
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
  setEditSession: (editSession) => set({ applyResult: null, changePlan: null, editSession }),
  setEditValidationDiagnostics: (editValidationDiagnostics) => set({ editValidationDiagnostics }),
  setEncounterSearchText: (encounterSearchText) => set({ encounterSearchText }),
  setExeFsPatchSearchText: (exeFsPatchSearchText) => set({ exeFsPatchSearchText }),
  setFlagworkSaveSearchText: (flagworkSaveSearchText) => set({ flagworkSaveSearchText }),
  setGiftPokemonSearchText: (giftPokemonSearchText) => set({ giftPokemonSearchText }),
  setPlacementSearchText: (placementSearchText) => set({ placementSearchText }),
  setPokemonSearchText: (pokemonSearchText) => set({ pokemonSearchText }),
  setRaidRewardSearchText: (raidRewardSearchText) => set({ raidRewardSearchText }),
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        movesSearchText: '',
        movesWorkflow,
        selectedMoveId
      };
    }),
  setPokemonWorkflow: (pokemonWorkflow) =>
    set((state) => {
      const selectedPokemonPersonalId = pokemonWorkflow.pokemon.some(
        (pokemon) => pokemon.personalId === state.selectedPokemonPersonalId
      )
        ? state.selectedPokemonPersonalId
        : (pokemonWorkflow.pokemon[0]?.personalId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'pokemon'),
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        giftPokemonSearchText: '',
        giftPokemonWorkflow,
        selectedGiftPokemonIndex
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
      encounterSearchText: '',
      encountersWorkflow: null,
      exeFsPatchSearchText: '',
      exeFsPatchWorkflow: null,
      flagworkSaveSearchText: '',
      flagworkSaveWorkflow: null,
      giftPokemonSearchText: '',
      giftPokemonWorkflow: null,
      itemSearchText: '',
      itemsWorkflow: null,
      movesSearchText: '',
      movesWorkflow: null,
      openProject,
      placementSearchText: '',
      placementWorkflow: null,
      pokemonSearchText: '',
      pokemonWorkflow: null,
      projectStatus: 'open',
      raidRewardSearchText: '',
      raidRewardsWorkflow: null,
      royalCandySearchText: '',
      royalCandyWorkflow: null,
      spreadsheetImportPreview: null,
      spreadsheetImportSearchText: '',
      spreadsheetImportSourcePath: '',
      spreadsheetImportWorkflow: null,
      selectedEncounterTableId: null,
      selectedExeFsCheckId: null,
      selectedExeFsPatchId: null,
      selectedGiftPokemonIndex: null,
      selectedRoyalCandyCheckId: null,
      selectedRoyalCandyWorkflowId: null,
      selectedSpreadsheetImportProfileId: null,
      selectedFlagId: null,
      selectedItemId: null,
      selectedMoveId: null,
      selectedPokemonPersonalId: null,
      selectedPlacementObjectId: null,
      selectedRaidRewardTableId: null,
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
  setSelectedRaidRewardTableId: (selectedRaidRewardTableId) =>
    set({ selectedRaidRewardTableId }),
  setSelectedPlacementObjectId: (selectedPlacementObjectId) =>
    set({ selectedPlacementObjectId }),
  setSelectedFlagId: (selectedFlagId) => set({ selectedFlagId }),
  setSelectedSaveBlockId: (selectedSaveBlockId) => set({ selectedSaveBlockId }),
  setSelectedExeFsCheckId: (selectedExeFsCheckId) => set({ selectedExeFsCheckId }),
  setSelectedExeFsPatchId: (selectedExeFsPatchId) => set({ selectedExeFsPatchId }),
  setSelectedGiftPokemonIndex: (selectedGiftPokemonIndex) =>
    set({ selectedGiftPokemonIndex }),
  setSelectedRoyalCandyCheckId: (selectedRoyalCandyCheckId) =>
    set({ selectedRoyalCandyCheckId }),
  setSelectedRoyalCandyWorkflowId: (selectedRoyalCandyWorkflowId) =>
    set({ selectedRoyalCandyWorkflowId }),
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        selectedShopId,
        shopSearchText: '',
        shopsWorkflow
      };
    }),
  setEncountersWorkflow: (encountersWorkflow) =>
    set((state) => {
      const selectedEncounterTableId = encountersWorkflow.tables.some(
        (table) => table.tableId === state.selectedEncounterTableId
      )
        ? state.selectedEncounterTableId
        : (encountersWorkflow.tables[0]?.tableId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'encounters'),
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        encounterSearchText: '',
        encountersWorkflow,
        selectedEncounterTableId
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        raidRewardSearchText: '',
        raidRewardsWorkflow,
        selectedRaidRewardTableId
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        placementSearchText: '',
        placementWorkflow,
        selectedPlacementObjectId
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        flagworkSaveSearchText: '',
        flagworkSaveWorkflow,
        selectedFlagId,
        selectedSaveBlockId
      };
    }),
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
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
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        royalCandySearchText: '',
        royalCandyWorkflow,
        selectedRoyalCandyCheckId,
        selectedRoyalCandyWorkflowId
      };
    }),
  setSpreadsheetImportWorkflow: (spreadsheetImportWorkflow) =>
    set((state) => {
      const selectedSpreadsheetImportProfileId = spreadsheetImportWorkflow.profiles.some(
        (profile) => profile.profileId === state.selectedSpreadsheetImportProfileId
      )
        ? state.selectedSpreadsheetImportProfileId
        : (spreadsheetImportWorkflow.profiles[0]?.profileId ?? null);

      return {
        activeSection: resolveWorkflowLoadSection(state.activeSection, 'spreadsheetImport'),
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        selectedSpreadsheetImportProfileId,
        spreadsheetImportPreview: null,
        spreadsheetImportSearchText: '',
        spreadsheetImportWorkflow
      };
    }),
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
      saveFilePath: typeof parsedValue.saveFilePath === 'string' ? parsedValue.saveFilePath : ''
    };
  } catch {
    return emptyDraft;
  }
}

function saveProjectPathDraft(draftPaths: ProjectPathDraft) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(projectPathDraftStorageKey, JSON.stringify(draftPaths));
  } catch {
    // Storage can be unavailable in hardened browser contexts; typed paths should still work.
  }
}

function createEmptyProjectPathDraft(): ProjectPathDraft {
  return {
    baseExeFsPath: '',
    baseRomFsPath: '',
    outputRootPath: '',
    saveFilePath: ''
  };
}
