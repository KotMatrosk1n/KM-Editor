/* SPDX-License-Identifier: GPL-3.0-only */
import { useState } from 'react';
import { type ApiDiagnostic, type EditSession } from '../../bridge/contracts';
import {
  type NpcItemGiftSelection,
  type NpcItemGiftWorkflow
} from '../../bridge/npcItemGiftContracts';
import { type ProjectBridge } from '../../bridge/projectBridge';

type NpcItemGiftPaths = Parameters<ProjectBridge['loadNpcItemGiftWorkflow']>[0]['paths'];

export function useNpcItemGiftWorkflowController({
  bridge,
  editSession,
  markClean,
  onDiagnostics,
  onError,
  onPanelDiagnostics,
  onSession,
  onWorkflow,
  paths,
  prepareStage
}: {
  bridge: ProjectBridge;
  editSession: EditSession | null;
  markClean: () => void;
  onDiagnostics: (diagnostics: ApiDiagnostic[]) => void;
  onError: (error: unknown) => void;
  onPanelDiagnostics: (diagnostics: ApiDiagnostic[]) => void;
  onSession: (session: EditSession) => void;
  onWorkflow: (workflow: NpcItemGiftWorkflow) => void;
  paths: NpcItemGiftPaths;
  prepareStage: () => void;
}) {
  const [isLoading, setIsLoading] = useState(false);
  const [isStaging, setIsStaging] = useState(false);

  const open = async () => {
    setIsLoading(true);
    onDiagnostics([]);

    try {
      const response = await bridge.loadNpcItemGiftWorkflow({ paths });
      onWorkflow(response.workflow);
    } catch (error) {
      onError(error);
    } finally {
      setIsLoading(false);
    }
  };

  const stage = async (gifts: NpcItemGiftSelection[]) => {
    setIsStaging(true);
    prepareStage();

    try {
      const response = await bridge.stageNpcItemGift({ gifts, paths, session: editSession });
      onWorkflow(response.workflow);
      onSession(response.session);
      onPanelDiagnostics(response.diagnostics);
      markClean();
    } catch (error) {
      onError(error);
    } finally {
      setIsStaging(false);
    }
  };

  return { isLoading, isStaging, open, stage };
}
