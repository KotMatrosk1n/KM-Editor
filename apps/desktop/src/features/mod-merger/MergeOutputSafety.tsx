// SPDX-License-Identifier: GPL-3.0-only
import { useMemo } from 'react';
import type { ApiDiagnostic, ProjectGame } from '../../bridge/contracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { OutputSafetyPanel } from '../output-safety/OutputSafetyPanel';
import { useOutputSafetyController } from '../output-safety/useOutputSafetyController';

export function MergeOutputSafety({ bridge, game, projectId, outputRoot, busy, onBusyChange, armWriteGuard, active }: {
  active: boolean;
  armWriteGuard: () => Promise<boolean>;
  bridge: ProjectBridge; game: ProjectGame; projectId: string; outputRoot: string;
  busy: boolean; onBusyChange: (busy: boolean) => void;
}) {
  const scope = useMemo(() => ({ projectId, paths: {
    selectedGame: game, outputRootPath: outputRoot, baseRomFsPath: null, baseExeFsPath: null,
    saveFilePath: null, scarletVioletSupportFolderPath: null, pokemonLegendsZASupportFolderPath: null,
    gameTextLanguage: null
  } }), [game, outputRoot, projectId]);
  const controller = useOutputSafetyController({ bridge, scope, externalMutationBusy: busy,
    armCriticalWriteGuard: armWriteGuard, onMutationBusyChange: onBusyChange });
  const diagnostics = (values: ApiDiagnostic[]) => values.map(value => ({ ...value, domain: 'workflow.modMerger' }));
  const scoped = <T extends { diagnostics: ApiDiagnostic[] },>(value: T | null): T | null => value && { ...value, diagnostics: diagnostics(value.diagnostics) };
  return active ? <OutputSafetyPanel controller={{ ...controller,
    actionDiagnostics: diagnostics(controller.actionDiagnostics), recoveryStatus: scoped(controller.recoveryStatus),
    integrity: scoped(controller.integrity), cleanupPreview: scoped(controller.cleanupPreview), cleanupResult: scoped(controller.cleanupResult),
    checkpointRestorePreview: scoped(controller.checkpointRestorePreview), restoreResult: scoped(controller.restoreResult)
  }} /> : null;
}
