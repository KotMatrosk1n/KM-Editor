/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect } from 'react';
import type { GameModuleProjectBridgeApi } from '../../bridge/gameModuleProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';
import type { WorkbenchSection } from '../../workbench/workbenchSections';
import type { SemanticQueryStatus } from '../semantic-explore/useSemanticExploreController';
import { GameModulesSection } from './GameModulesSection';
import { useGameModuleController } from './useGameModuleController';
import './gameModules.css';

export type GameModulesRuntimeProps = {
  bridge: GameModuleProjectBridgeApi;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  canOpenSection: (section: WorkbenchSection) => boolean;
  capabilityStatus: SemanticQueryStatus;
  onEnsureCapabilities: () => Promise<void>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onOpenSection: (section: WorkbenchSection) => void;
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export default function GameModulesRuntime({
  bridge,
  canNavigateRecord,
  canOpenSection,
  capabilityStatus,
  onEnsureCapabilities,
  onNavigateRecord,
  onOpenSection,
  onStaleRevision,
  revision,
  scope
}: GameModulesRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') {
      void onEnsureCapabilities();
    }
  }, [capabilityStatus, onEnsureCapabilities, revision]);

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
        <div
          aria-live="polite"
          className="km-game-module-status"
          role={isError ? 'alert' : 'status'}
        >
          <p>{t(isError ? 'gameModules.error' : 'gameModules.loading')}</p>
          {isError ? (
            <button onClick={() => void onEnsureCapabilities()} type="button">
              {t('gameModules.retry')}
            </button>
          ) : null}
        </div>
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
  onStaleRevision,
  revision,
  scope
}: Omit<
  GameModulesRuntimeProps,
  'capabilityStatus' | 'onEnsureCapabilities' | 'revision'
> & { revision: SemanticExploreRevision }) {
  const controller = useGameModuleController({
    bridge,
    onStaleRevision,
    revision,
    scope
  });
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
