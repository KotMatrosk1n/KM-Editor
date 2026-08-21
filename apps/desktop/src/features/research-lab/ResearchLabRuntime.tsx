/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect } from 'react';
import type { ResearchLabProjectBridgeApi } from '../../bridge/researchLabProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';
import type { SemanticQueryStatus } from '../semantic-explore/useSemanticExploreController';
import { ResearchLabSection } from './ResearchLabSection';
import { useResearchLabController } from './useResearchLabController';
import './researchLab.css';

export type ResearchLabRuntimeProps = {
  bridge: ResearchLabProjectBridgeApi;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  capabilityStatus: SemanticQueryStatus;
  onEnsureCapabilities: () => Promise<void>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onPickSource: (slot: 0 | 1) => Promise<string | null>;
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export default function ResearchLabRuntime({
  bridge,
  canNavigateRecord,
  capabilityStatus,
  onEnsureCapabilities,
  onNavigateRecord,
  onPickSource,
  onStaleRevision,
  revision,
  scope
}: ResearchLabRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') void onEnsureCapabilities();
  }, [capabilityStatus, onEnsureCapabilities, revision]);

  if (!revision) {
    const isError = capabilityStatus === 'error' || capabilityStatus === 'ready';
    return (
      <section aria-labelledby="research-lab-title" className="km-research-lab wide-panel">
        <header className="km-research-lab-heading">
          <div>
            <p>{t('researchLab.eyebrow')}</p>
            <h2 id="research-lab-title">{t('researchLab.title')}</h2>
            <span>{t('researchLab.description')}</span>
          </div>
        </header>
        <div
          aria-live="polite"
          className="km-research-lab-status"
          role={isError ? 'alert' : 'status'}
        >
          <p>{t(isError ? 'researchLab.error.generic' : 'researchLab.loading')}</p>
          {isError ? (
            <button onClick={() => void onEnsureCapabilities()} type="button">
              {t('researchLab.retry')}
            </button>
          ) : null}
        </div>
      </section>
    );
  }

  return (
    <ResearchLabReadyRuntime
      bridge={bridge}
      canNavigateRecord={canNavigateRecord}
      onNavigateRecord={onNavigateRecord}
      onPickSource={onPickSource}
      onStaleRevision={onStaleRevision}
      revision={revision}
      scope={scope}
    />
  );
}

function ResearchLabReadyRuntime({
  bridge,
  canNavigateRecord,
  onNavigateRecord,
  onPickSource,
  onStaleRevision,
  revision,
  scope
}: Omit<
  ResearchLabRuntimeProps,
  'capabilityStatus' | 'onEnsureCapabilities' | 'revision'
> & { revision: SemanticExploreRevision }) {
  const controller = useResearchLabController({
    bridge,
    onStaleRevision,
    revision,
    scope
  });

  useEffect(() => {
    if (controller.capabilities.status === 'idle') void controller.loadCapabilities();
  }, [controller.capabilities.status, controller.loadCapabilities]);
  useEffect(() => {
    if (
      controller.capabilities.status === 'ready' &&
      controller.annotations.status === 'idle'
    ) {
      void controller.loadAnnotations();
    }
  }, [
    controller.annotations.status,
    controller.capabilities.status,
    controller.loadAnnotations
  ]);

  return (
    <ResearchLabSection
      canNavigateRecord={canNavigateRecord}
      controller={controller}
      onNavigateRecord={onNavigateRecord}
      onPickSource={onPickSource}
      revision={revision}
    />
  );
}
