/* SPDX-License-Identifier: GPL-3.0-only */

import { create } from 'zustand';
import {
  type ItemsWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
  type WorkflowSummary
} from './bridge/contracts';

export type WorkbenchSection = 'health' | 'workflows' | 'items' | 'changes';

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
  draftPaths: ProjectPathDraft;
  itemSearchText: string;
  itemsWorkflow: ItemsWorkflow | null;
  openProject: OpenProjectState | null;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
  selectedItemId: number | null;
  workflows: WorkflowSummary[];
  setDraftPath: (field: keyof ProjectPathDraft, value: string) => void;
  setActiveSection: (activeSection: WorkbenchSection) => void;
  setItemsWorkflow: (itemsWorkflow: ItemsWorkflow) => void;
  setItemSearchText: (itemSearchText: string) => void;
  setOpenProject: (project: OpenProjectState) => void;
  setProjectHealth: (health: ProjectHealth) => void;
  setProjectStatus: (projectStatus: WorkbenchState['projectStatus']) => void;
  setSelectedItemId: (selectedItemId: number | null) => void;
  setWorkflows: (workflows: WorkflowSummary[]) => void;
};

export const useWorkbenchStore = create<WorkbenchState>((set) => ({
  activeSection: 'health',
  draftPaths: {
    baseExeFsPath: '',
    baseRomFsPath: '',
    outputRootPath: ''
  },
  itemSearchText: '',
  itemsWorkflow: null,
  openProject: null,
  projectStatus: 'idle',
  selectedItemId: null,
  workflows: [],
  setActiveSection: (activeSection) => set({ activeSection }),
  setDraftPath: (field, value) =>
    set((state) => ({
      draftPaths: {
        ...state.draftPaths,
        [field]: value
      }
    })),
  setItemsWorkflow: (itemsWorkflow) =>
    set({
      activeSection: 'items',
      itemSearchText: '',
      itemsWorkflow,
      selectedItemId: itemsWorkflow.items[0]?.itemId ?? null
    }),
  setItemSearchText: (itemSearchText) => set({ itemSearchText }),
  setOpenProject: (openProject) => set({ openProject, projectStatus: 'open' }),
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
  setWorkflows: (workflows) => set({ workflows })
}));
