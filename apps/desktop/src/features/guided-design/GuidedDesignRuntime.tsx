/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect } from 'react';
import type {
  GuidedDesignImportRequest,
  GuidedDesignImportResponse
} from '../../bridge/guidedDesignContracts';
import type { GuidedDesignProjectBridgeApi } from '../../bridge/guidedDesignProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { PublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';
import type {
  SemanticQueryError,
  SemanticQueryStatus
} from '../semantic-explore/useSemanticExploreController';
import { GuidedDesignSection } from './GuidedDesignSection';
import { useGuidedDesignController } from './useGuidedDesignController';
import {
  createAnalysisPreparationProgress,
  preparationProgressFromQueryStatuses,
  type AnalysisPreparationProgress
} from '../workbench/analysisPreparation';

export type GuidedDesignRuntimeProps = {
  authoringContextRevision: string | null;
  bridge: GuidedDesignProjectBridgeApi;
  capabilityError: SemanticQueryError | null;
  capabilityStatus: SemanticQueryStatus;
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  expectedChangeSetETag: string | null;
  isChangeSetWorkspaceBusy: boolean;
  isChangeSetWorkspaceReady: boolean;
  onEnsureCapabilities: () => Promise<void>;
  onImportProposal: (
    request: GuidedDesignImportRequest
  ) => Promise<GuidedDesignImportResponse>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onOpenChanges: () => void;
  onPreparationStateChange?: (progress: AnalysisPreparationProgress) => void;
  onRefreshCapabilities: () => Promise<void>;
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export function GuidedDesignRuntime({
  authoringContextRevision,
  bridge,
  capabilityError,
  capabilityStatus,
  canImportChangeSet,
  canNavigateRecord,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onEnsureCapabilities,
  onImportProposal,
  onNavigateRecord,
  onOpenChanges,
  onPreparationStateChange,
  onRefreshCapabilities,
  onStaleRevision,
  revision,
  scope
}: GuidedDesignRuntimeProps) {
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
          'guidedDesign',
          capabilityStatus === 'error' || capabilityStatus === 'ready' ? 'error' : 'loading'
        )
      );
    }
  }, [capabilityStatus, onPreparationStateChange, revision]);

  if (!revision) {
    const isError = capabilityStatus === 'error' || capabilityStatus === 'ready';
    return (
      <section aria-labelledby="guided-design-title" className="km-guided-design wide-panel">
        <header className="km-guided-heading">
          <div>
            <p>{t('guidedDesign.eyebrow')}</p>
            <h2 id="guided-design-title">{t('guidedDesign.title')}</h2>
            <span>{t('guidedDesign.description')}</span>
          </div>
        </header>
        {isError ? (
          <>
            <PublishCommonEditorError
              domain="analysis.guidedDesign"
              message={t(capabilityError
                ? `semanticExplore.query.error.${capabilityError}`
                : 'guidedDesign.error.generic')}
            />
            <div aria-live="polite" className="km-guided-status" role="alert">
              <p>{t(capabilityError
                ? `semanticExplore.query.error.${capabilityError}`
                : 'guidedDesign.error.generic')}</p>
              <button onClick={() => void onRefreshCapabilities()} type="button">
                {t('guidedDesign.retry')}
              </button>
            </div>
          </>
        ) : (
          <div className="km-guided-status">
            <LoadingProgress label={t('guidedDesign.capabilities.loading')} />
          </div>
        )}
      </section>
    );
  }

  return (
    <GuidedDesignReadyRuntime
      authoringContextRevision={authoringContextRevision}
      bridge={bridge}
      canImportChangeSet={canImportChangeSet}
      canNavigateRecord={canNavigateRecord}
      expectedChangeSetETag={expectedChangeSetETag}
      isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
      isChangeSetWorkspaceReady={isChangeSetWorkspaceReady}
      onImportProposal={onImportProposal}
      onNavigateRecord={onNavigateRecord}
      onOpenChanges={onOpenChanges}
      onPreparationStateChange={onPreparationStateChange}
      onStaleRevision={onStaleRevision}
      revision={revision}
      scope={scope}
    />
  );
}

function GuidedDesignReadyRuntime({
  authoringContextRevision,
  bridge,
  canImportChangeSet,
  canNavigateRecord,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onImportProposal,
  onNavigateRecord,
  onOpenChanges,
  onPreparationStateChange,
  onStaleRevision,
  revision,
  scope
}: Omit<
  GuidedDesignRuntimeProps,
  | 'capabilityError'
  | 'capabilityStatus'
  | 'onEnsureCapabilities'
  | 'onRefreshCapabilities'
  | 'revision'
> & {
  revision: SemanticExploreRevision;
}) {
  const controller = useGuidedDesignController({
    authoringContextRevision,
    bridge,
    expectedChangeSetETag,
    isAuthoringContextReady: isChangeSetWorkspaceReady,
    onStaleRevision,
    revision,
    scope
  });

  useEffect(() => {
    void controller.ensureCapabilities();
  }, [
    authoringContextRevision,
    bridge,
    controller.ensureCapabilities,
    expectedChangeSetETag,
    revision,
    scope
  ]);
  useEffect(() => {
    onPreparationStateChange?.(
      preparationProgressFromQueryStatuses('guidedDesign', [controller.capabilities.status])
    );
  }, [controller.capabilities.status, onPreparationStateChange]);

  return (
    <GuidedDesignSection
      canImportChangeSet={canImportChangeSet}
      canNavigateRecord={canNavigateRecord}
      controller={controller}
      expectedChangeSetETag={expectedChangeSetETag}
      isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
      isChangeSetWorkspaceReady={isChangeSetWorkspaceReady}
      onImportProposal={onImportProposal}
      onNavigateRecord={onNavigateRecord}
      onOpenChanges={onOpenChanges}
      revision={revision}
      scope={scope}
    />
  );
}
