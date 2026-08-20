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
import { useLocalization } from '../../localization';
import type { SemanticQueryStatus } from '../semantic-explore/useSemanticExploreController';
import { GuidedDesignSection } from './GuidedDesignSection';
import { useGuidedDesignController } from './useGuidedDesignController';

export type GuidedDesignRuntimeProps = {
  authoringContextRevision: string | null;
  bridge: GuidedDesignProjectBridgeApi;
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
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export function GuidedDesignRuntime({
  authoringContextRevision,
  bridge,
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
  onStaleRevision,
  revision,
  scope
}: GuidedDesignRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') {
      void onEnsureCapabilities();
    }
  }, [capabilityStatus, onEnsureCapabilities, revision]);

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
        <div
          aria-live="polite"
          className="km-guided-status"
          role={isError ? 'alert' : 'status'}
        >
          <p>{t(isError
            ? 'guidedDesign.error.generic'
            : 'guidedDesign.capabilities.loading')}</p>
          {isError ? (
            <button onClick={() => void onEnsureCapabilities()} type="button">
              {t('guidedDesign.retry')}
            </button>
          ) : null}
        </div>
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
  onStaleRevision,
  revision,
  scope
}: Omit<GuidedDesignRuntimeProps, 'capabilityStatus' | 'onEnsureCapabilities' | 'revision'> & {
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
