/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useState } from 'react';
import { type ApiDiagnostic } from '../bridge/contracts';
import { type WorkbenchSection } from '../workbenchStore';
import { type WorkflowPanelOutput } from './workflowPanels';

export type ScopedEditorPanelState = WorkflowPanelOutput & {
  changePlanSessionSignature: string | null;
};

export const scopedEditorPanelSectionIds = new Set<WorkbenchSection>([
  'bagHook',
  'royalCandy',
  'startingItems',
  'catchCap',
  'ivScreen',
  'hyperTraining',
  'shinyRate',
  'typeChart',
  'fairyGymBoosts',
  'fashionUnlock',
  'gymUniformRemoval'
]);

export function useScopedEditorPanelOutput(currentEditSessionSignature: string | null) {
  const [scopedEditorPanelStates, setScopedEditorPanelStates] = useState<
    Partial<Record<WorkbenchSection, ScopedEditorPanelState>>
  >({});

  const getScopedEditorPanelOutput = useCallback(
    (section: WorkbenchSection): WorkflowPanelOutput => {
      const state = scopedEditorPanelStates[section];
      const isPlanCurrent =
        state?.changePlan !== null &&
        state?.changePlan !== undefined &&
        currentEditSessionSignature !== null &&
        currentEditSessionSignature === state.changePlanSessionSignature;

      return {
        actionDiagnostics: state?.actionDiagnostics ?? [],
        applyResult: state?.applyResult ?? null,
        changePlan: isPlanCurrent ? state.changePlan : null
      };
    },
    [currentEditSessionSignature, scopedEditorPanelStates]
  );

  const clearScopedEditorPanelState = useCallback((section?: WorkbenchSection) => {
    if (section === undefined) {
      setScopedEditorPanelStates({});
      return;
    }

    setScopedEditorPanelStates((currentStates) => {
      if (!(section in currentStates)) {
        return currentStates;
      }

      const nextStates = { ...currentStates };
      delete nextStates[section];
      return nextStates;
    });
  }, []);

  const setScopedEditorPanelDiagnostics = useCallback(
    (section: WorkbenchSection, diagnostics: ApiDiagnostic[]) => {
      setScopedEditorPanelStates((currentStates) => ({
        ...currentStates,
        [section]: {
          actionDiagnostics: diagnostics,
          applyResult: null,
          changePlan: null,
          changePlanSessionSignature: null
        }
      }));
    },
    []
  );

  return {
    clearScopedEditorPanelState,
    getScopedEditorPanelOutput,
    scopedEditorPanelStates,
    setScopedEditorPanelDiagnostics,
    setScopedEditorPanelStates
  };
}
