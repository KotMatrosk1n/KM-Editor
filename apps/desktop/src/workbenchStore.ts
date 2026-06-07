/* SPDX-License-Identifier: GPL-3.0-only */

import { create } from 'zustand';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type ChangePlan,
  type EditSession,
  type EncountersWorkflow,
  type ItemsWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
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
  itemSearchText: string;
  itemsWorkflow: ItemsWorkflow | null;
  openProject: OpenProjectState | null;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
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
  setItemsWorkflow: (itemsWorkflow: ItemsWorkflow) => void;
  setItemSearchText: (itemSearchText: string) => void;
  setOpenProject: (project: OpenProjectState) => void;
  setProjectHealth: (health: ProjectHealth) => void;
  setProjectStatus: (projectStatus: WorkbenchState['projectStatus']) => void;
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
  itemSearchText: '',
  itemsWorkflow: null,
  openProject: null,
  projectStatus: 'idle',
  selectedItemId: null,
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
      itemSearchText: '',
      itemsWorkflow: null,
      openProject,
      projectStatus: 'open',
      selectedEncounterTableId: null,
      selectedItemId: null,
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
  setWorkflows: (workflows) => set({ workflows })
}));
