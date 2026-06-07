/* SPDX-License-Identifier: GPL-3.0-only */

import { create } from 'zustand';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type ChangePlan,
  type EditSession,
  type ItemsWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
  type TextWorkflow,
  type WorkflowSummary
} from './bridge/contracts';

export type WorkbenchSection = 'health' | 'workflows' | 'items' | 'text' | 'changes';

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
  itemSearchText: string;
  itemsWorkflow: ItemsWorkflow | null;
  openProject: OpenProjectState | null;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
  selectedItemId: number | null;
  selectedTextKey: string | null;
  textSearchText: string;
  textWorkflow: TextWorkflow | null;
  workflows: WorkflowSummary[];
  setDraftPath: (field: keyof ProjectPathDraft, value: string) => void;
  setActiveSection: (activeSection: WorkbenchSection) => void;
  setApplyResult: (applyResult: ApplyResult | null) => void;
  setChangePlan: (changePlan: ChangePlan | null) => void;
  setEditSession: (editSession: EditSession | null) => void;
  setEditValidationDiagnostics: (diagnostics: ApiDiagnostic[]) => void;
  setItemsWorkflow: (itemsWorkflow: ItemsWorkflow) => void;
  setItemSearchText: (itemSearchText: string) => void;
  setOpenProject: (project: OpenProjectState) => void;
  setProjectHealth: (health: ProjectHealth) => void;
  setProjectStatus: (projectStatus: WorkbenchState['projectStatus']) => void;
  setSelectedItemId: (selectedItemId: number | null) => void;
  setSelectedTextKey: (selectedTextKey: string | null) => void;
  setTextSearchText: (textSearchText: string) => void;
  setTextWorkflow: (textWorkflow: TextWorkflow) => void;
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
  itemSearchText: '',
  itemsWorkflow: null,
  openProject: null,
  projectStatus: 'idle',
  selectedItemId: null,
  selectedTextKey: null,
  textSearchText: '',
  textWorkflow: null,
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
  setTextSearchText: (textSearchText) => set({ textSearchText }),
  setOpenProject: (openProject) =>
    set({
      applyResult: null,
      changePlan: null,
      editSession: null,
      editValidationDiagnostics: [],
      itemSearchText: '',
      itemsWorkflow: null,
      openProject,
      projectStatus: 'open',
      selectedItemId: null,
      selectedTextKey: null,
      textSearchText: '',
      textWorkflow: null
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
  setSelectedItemId: (selectedItemId) => set({ selectedItemId }),
  setSelectedTextKey: (selectedTextKey) => set({ selectedTextKey }),
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
  setWorkflows: (workflows) => set({ workflows })
}));
