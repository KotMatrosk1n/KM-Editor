/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect } from 'react';
import type { ResearchLabProjectBridgeApi } from '../../bridge/researchLabProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';
import type {
  SemanticQueryError,
  SemanticQueryStatus
} from '../semantic-explore/useSemanticExploreController';
import { ResearchLabSection } from './ResearchLabSection';
import { useResearchLabController } from './useResearchLabController';
import './researchLab.css';
import {
  preparationStateFromQueryStatus,
  type AnalysisPreparationState
} from '../workbench/analysisPreparation';

export type ResearchLabRuntimeProps = {
  bridge: ResearchLabProjectBridgeApi;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  capabilityError: SemanticQueryError | null;
  capabilityStatus: SemanticQueryStatus;
  onEnsureCapabilities: () => Promise<void>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onPickSource: (slot: 0 | 1) => Promise<string | null>;
  onPreparationStateChange?: (state: AnalysisPreparationState) => void;
  onRefreshCapabilities: () => Promise<void>;
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export default function ResearchLabRuntime({
  bridge,
  canNavigateRecord,
  capabilityError,
  capabilityStatus,
  onEnsureCapabilities,
  onNavigateRecord,
  onPickSource,
  onPreparationStateChange,
  onRefreshCapabilities,
  onStaleRevision,
  revision,
  scope
}: ResearchLabRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') void onEnsureCapabilities();
  }, [capabilityStatus, onEnsureCapabilities, revision, scope]);
  useEffect(() => {
    if (!revision) {
      onPreparationStateChange?.(
        capabilityStatus === 'error' || capabilityStatus === 'ready' ? 'error' : 'loading'
      );
    }
  }, [capabilityStatus, onPreparationStateChange, revision]);

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
        {isError ? (
          <div aria-live="polite" className="km-research-lab-status" role="alert">
            <p>{t(capabilityError
              ? `semanticExplore.query.error.${capabilityError}`
              : 'researchLab.error.generic')}</p>
            <button onClick={() => void onRefreshCapabilities()} type="button">
              {t('researchLab.retry')}
            </button>
          </div>
        ) : (
          <div className="km-research-lab-status">
            <LoadingProgress label={t('researchLab.loading')} />
          </div>
        )}
      </section>
    );
  }

  return (
    <ResearchLabReadyRuntime
      bridge={bridge}
      canNavigateRecord={canNavigateRecord}
      onNavigateRecord={onNavigateRecord}
      onPickSource={onPickSource}
      onPreparationStateChange={onPreparationStateChange}
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
  onPreparationStateChange,
  onStaleRevision,
  revision,
  scope
}: Omit<
  ResearchLabRuntimeProps,
  | 'capabilityError'
  | 'capabilityStatus'
  | 'onEnsureCapabilities'
  | 'onRefreshCapabilities'
  | 'revision'
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
  useEffect(() => {
    const capabilityState = preparationStateFromQueryStatus(controller.capabilities.status);
    const annotationState = preparationStateFromQueryStatus(controller.annotations.status);
    onPreparationStateChange?.(
      capabilityState === 'error' || annotationState === 'error'
        ? 'error'
        : capabilityState === 'ready' && annotationState === 'ready'
          ? 'ready'
          : 'loading'
    );
  }, [
    controller.annotations.status,
    controller.capabilities.status,
    onPreparationStateChange
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
