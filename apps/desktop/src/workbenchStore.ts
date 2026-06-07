/* SPDX-License-Identifier: GPL-3.0-only */

import { create } from 'zustand';

export type WorkbenchSection = 'health' | 'workflows' | 'changes';

type WorkbenchState = {
  activeSection: WorkbenchSection;
  setActiveSection: (activeSection: WorkbenchSection) => void;
};

export const useWorkbenchStore = create<WorkbenchState>((set) => ({
  activeSection: 'health',
  setActiveSection: (activeSection) => set({ activeSection })
}));
