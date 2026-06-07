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
  type ItemsWorkflow,
  type PlacementWorkflow,
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
  | 'text'
  | 'trainers'
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
};

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
  itemSearchText: string;
  itemsWorkflow: ItemsWorkflow | null;
  openProject: OpenProjectState | null;
  placementSearchText: string;
  placementWorkflow: PlacementWorkflow | null;
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
  selectedRoyalCandyCheckId: string | null;
  selectedRoyalCandyWorkflowId: string | null;
  selectedSpreadsheetImportProfileId: string | null;
  selectedItemId: number | null;
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
  setItemsWorkflow: (itemsWorkflow: ItemsWorkflow) => void;
  setItemSearchText: (itemSearchText: string) => void;
  setOpenProject: (project: OpenProjectState) => void;
  setPlacementSearchText: (placementSearchText: string) => void;
  setPlacementWorkflow: (placementWorkflow: PlacementWorkflow) => void;
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
  setSelectedRoyalCandyCheckId: (selectedRoyalCandyCheckId: string | null) => void;
  setSelectedRoyalCandyWorkflowId: (selectedRoyalCandyWorkflowId: string | null) => void;
  setSelectedSpreadsheetImportProfileId: (
    selectedSpreadsheetImportProfileId: string | null
  ) => void;
  setSelectedItemId: (selectedItemId: number | null) => void;
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

export const useWorkbenchStore = create<WorkbenchState>((set) => ({
  activeSection: 'health',
  applyResult: null,
  changePlan: null,
  draftPaths: {
    baseExeFsPath: '',
    baseRomFsPath: '',
    outputRootPath: ''
  },
  editSession: null,
  editValidationDiagnostics: [],
  encounterSearchText: '',
  encountersWorkflow: null,
  exeFsPatchSearchText: '',
  exeFsPatchWorkflow: null,
  flagworkSaveSearchText: '',
  flagworkSaveWorkflow: null,
  itemSearchText: '',
  itemsWorkflow: null,
  openProject: null,
  placementSearchText: '',
  placementWorkflow: null,
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
  selectedRoyalCandyCheckId: null,
  selectedRoyalCandyWorkflowId: null,
  selectedSpreadsheetImportProfileId: null,
  selectedItemId: null,
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
    set((state) => ({
      draftPaths: {
        ...state.draftPaths,
        [field]: value
      }
    })),
  setEditSession: (editSession) => set({ applyResult: null, changePlan: null, editSession }),
  setEditValidationDiagnostics: (editValidationDiagnostics) => set({ editValidationDiagnostics }),
  setEncounterSearchText: (encounterSearchText) => set({ encounterSearchText }),
  setExeFsPatchSearchText: (exeFsPatchSearchText) => set({ exeFsPatchSearchText }),
  setFlagworkSaveSearchText: (flagworkSaveSearchText) => set({ flagworkSaveSearchText }),
  setPlacementSearchText: (placementSearchText) => set({ placementSearchText }),
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
        activeSection: 'items',
        applyResult: null,
        changePlan: null,
        editSession: null,
        editValidationDiagnostics: [],
        itemSearchText: '',
        itemsWorkflow,
        selectedItemId
      };
    }),
  setItemSearchText: (itemSearchText) => set({ itemSearchText }),
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
      itemSearchText: '',
      itemsWorkflow: null,
      openProject,
      placementSearchText: '',
      placementWorkflow: null,
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
      selectedRoyalCandyCheckId: null,
      selectedRoyalCandyWorkflowId: null,
      selectedSpreadsheetImportProfileId: null,
      selectedFlagId: null,
      selectedItemId: null,
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
  setSelectedRoyalCandyCheckId: (selectedRoyalCandyCheckId) =>
    set({ selectedRoyalCandyCheckId }),
  setSelectedRoyalCandyWorkflowId: (selectedRoyalCandyWorkflowId) =>
    set({ selectedRoyalCandyWorkflowId }),
  setSelectedSpreadsheetImportProfileId: (selectedSpreadsheetImportProfileId) =>
    set({ selectedSpreadsheetImportProfileId }),
  setSelectedEncounterTableId: (selectedEncounterTableId) => set({ selectedEncounterTableId }),
  setSelectedItemId: (selectedItemId) => set({ selectedItemId }),
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
        activeSection: 'text',
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
        activeSection: 'trainers',
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
        activeSection: 'shops',
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
        activeSection: 'encounters',
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
        activeSection: 'raidRewards',
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
        activeSection: 'placement',
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
        activeSection: 'flagworkSave',
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
        activeSection: 'exefsPatches',
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
        activeSection: 'royalCandy',
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
        activeSection: 'spreadsheetImport',
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
