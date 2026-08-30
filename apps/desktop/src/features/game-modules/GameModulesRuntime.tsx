/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect } from 'react';
import type { GameModuleProjectBridgeApi } from '../../bridge/gameModuleProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { PublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import type { WorkbenchSection } from '../../workbench/workbenchSections';
import type {
  SemanticQueryError,
  SemanticQueryStatus
} from '../semantic-explore/useSemanticExploreController';
import { GameModulesSection } from './GameModulesSection';
import { useGameModuleController } from './useGameModuleController';
import {
  createAnalysisPreparationProgress,
  preparationProgressFromQueryStatuses,
  type AnalysisPreparationProgress
} from '../workbench/analysisPreparation';
import './gameModules.css';

export type GameModulesRuntimeProps = {
  bridge: GameModuleProjectBridgeApi;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  canOpenSection: (section: WorkbenchSection) => boolean;
  capabilityError: SemanticQueryError | null;
  capabilityStatus: SemanticQueryStatus;
  onEnsureCapabilities: () => Promise<void>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onOpenSection: (section: WorkbenchSection) => void;
  onPreparationStateChange?: (progress: AnalysisPreparationProgress) => void;
  onRefreshCapabilities: () => Promise<void>;
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export default function GameModulesRuntime({
  bridge,
  canNavigateRecord,
  canOpenSection,
  capabilityError,
  capabilityStatus,
  onEnsureCapabilities,
  onNavigateRecord,
  onOpenSection,
  onPreparationStateChange,
  onRefreshCapabilities,
  onStaleRevision,
  revision,
  scope
}: GameModulesRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') {
      void onEnsureCapabilities();
    }
  }, [capabilityStatus, onEnsureCapabilities, revision, scope]);
  useEffect(() => {
    if (!revision) {
      onPreparationStateChange?.(
        createAnalysisPreparationProgress(
          'gameModules',
          capabilityStatus === 'error' || capabilityStatus === 'ready' ? 'error' : 'loading'
        )
      );
    }
  }, [capabilityStatus, onPreparationStateChange, revision]);

  if (!revision) {
    const isError = capabilityStatus === 'error' || capabilityStatus === 'ready';
    return (
      <section aria-labelledby="game-modules-title" className="km-game-modules wide-panel">
        <header className="km-game-modules-heading">
          <div>
            <p>{t('gameModules.eyebrow')}</p>
            <h2 id="game-modules-title">{t('gameModules.title')}</h2>
            <span>{t('gameModules.description')}</span>
          </div>
        </header>
        {isError ? (
          <>
          <PublishCommonEditorError
            domain="analysis.gameModules"
            message={t(capabilityError
              ? `semanticExplore.query.error.${capabilityError}`
              : 'gameModules.error')}
          />
          <div aria-live="polite" className="km-game-module-status" role="alert">
            <p>{t(capabilityError
              ? `semanticExplore.query.error.${capabilityError}`
              : 'gameModules.error')}</p>
            <button onClick={() => void onRefreshCapabilities()} type="button">
              {t('gameModules.retry')}
            </button>
          </div>
          </>
        ) : (
          <div className="km-game-module-status">
            <LoadingProgress label={t('gameModules.loading')} />
          </div>
        )}
      </section>
    );
  }

  return (
    <GameModulesReadyRuntime
      bridge={bridge}
      canNavigateRecord={canNavigateRecord}
      canOpenSection={canOpenSection}
      onNavigateRecord={onNavigateRecord}
      onOpenSection={onOpenSection}
      onPreparationStateChange={onPreparationStateChange}
      onStaleRevision={onStaleRevision}
      revision={revision}
      scope={scope}
    />
  );
}

function GameModulesReadyRuntime({
  bridge,
  canNavigateRecord,
  canOpenSection,
  onNavigateRecord,
  onOpenSection,
  onPreparationStateChange,
  onStaleRevision,
  revision,
  scope
}: Omit<
  GameModulesRuntimeProps,
  | 'capabilityError'
  | 'capabilityStatus'
  | 'onEnsureCapabilities'
  | 'onRefreshCapabilities'
  | 'revision'
> & { revision: SemanticExploreRevision }) {
  const controller = useGameModuleController({
    bridge,
    onStaleRevision,
    revision,
    scope
  });
  useEffect(() => {
    onPreparationStateChange?.(
      preparationProgressFromQueryStatuses('gameModules', [controller.capabilities.status])
    );
  }, [controller.capabilities.status, onPreparationStateChange]);
  return (
    <GameModulesSection
      canNavigateRecord={canNavigateRecord}
      canOpenSection={canOpenSection}
      controller={controller}
      onNavigateRecord={onNavigateRecord}
      onOpenSection={onOpenSection}
    />
  );
}
