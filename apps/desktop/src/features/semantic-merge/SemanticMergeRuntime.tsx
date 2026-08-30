/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect } from 'react';
import type {
  KmRecipeImportRequest,
  KmRecipeImportResponse,
  SemanticMergeImportRequest,
  SemanticMergeImportResponse
} from '../../bridge/semanticMergeContracts';
import type { SemanticMergeProjectBridgeApi } from '../../bridge/semanticMergeProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { PublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import type {
  SemanticQueryError,
  SemanticQueryStatus
} from '../semantic-explore/useSemanticExploreController';
import type { ExpectedImportedScalarEdit } from '../change-sets/useChangeSetWorkspaceController';
import { SemanticMergeSection } from './SemanticMergeSection';
import { useSemanticMergeController } from './useSemanticMergeController';
import {
  createAnalysisPreparationProgress,
  preparationProgressFromQueryStatuses,
  type AnalysisPreparationProgress
} from '../workbench/analysisPreparation';

export type SemanticMergeChangeSetOption = {
  archived: boolean;
  changeSetId: string;
  dependencyIds: readonly string[];
  enabled: boolean;
  name: string;
  operationCount: number;
  recipeExportDomain: string | null;
  recipeExportEligibility: 'eligible' | 'empty' | 'binding' | 'payload' | 'mixedDomain';
  recipeExportFieldKeys: readonly string[];
  recipeExportTargetKeys: readonly string[];
};

export type SemanticMergeRuntimeProps = {
  authoringContextRevision: string | null;
  bridge: SemanticMergeProjectBridgeApi;
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  capabilityError: SemanticQueryError | null;
  capabilityStatus: SemanticQueryStatus;
  changeSets: readonly SemanticMergeChangeSetOption[];
  expectedChangeSetETag: string | null;
  isChangeSetWorkspaceBusy: boolean;
  isChangeSetWorkspaceReady: boolean;
  onEnsureCapabilities: () => Promise<void>;
  onImportRecipe: (
    request: KmRecipeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => Promise<KmRecipeImportResponse>;
  onImportSemanticMerge: (
    request: SemanticMergeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => Promise<SemanticMergeImportResponse>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onOpenChanges: () => void;
  onPickSource: (slot: 'a' | 'b') => Promise<string | null>;
  onPreparationStateChange?: (progress: AnalysisPreparationProgress) => void;
  onRefreshCapabilities: () => Promise<void>;
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export function SemanticMergeRuntime({
  authoringContextRevision,
  bridge,
  canImportChangeSet,
  canNavigateRecord,
  capabilityError,
  capabilityStatus,
  changeSets,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onEnsureCapabilities,
  onImportRecipe,
  onImportSemanticMerge,
  onNavigateRecord,
  onOpenChanges,
  onPickSource,
  onPreparationStateChange,
  onRefreshCapabilities,
  onStaleRevision,
  revision,
  scope
}: SemanticMergeRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') void onEnsureCapabilities();
  }, [capabilityStatus, onEnsureCapabilities, revision, scope]);
  useEffect(() => {
    if (!revision) {
      onPreparationStateChange?.(
        createAnalysisPreparationProgress(
          'semanticMerge',
          capabilityStatus === 'error' || capabilityStatus === 'ready' ? 'error' : 'loading'
        )
      );
    }
  }, [capabilityStatus, onPreparationStateChange, revision]);

  if (!revision) {
    const isError = capabilityStatus === 'error' || capabilityStatus === 'ready';
    return (
      <section aria-labelledby="semantic-merge-title" className="km-semantic-merge wide-panel">
        <header className="km-semantic-merge-heading">
          <div>
            <p>{t('semanticMerge.eyebrow')}</p>
            <h2 id="semantic-merge-title">{t('semanticMerge.title')}</h2>
            <span>{t('semanticMerge.description')}</span>
          </div>
        </header>
        {isError ? (
          <>
          <PublishCommonEditorError
            domain="analysis.semanticMerge"
            message={t(capabilityError
              ? `semanticExplore.query.error.${capabilityError}`
              : 'semanticMerge.error.generic')}
          />
          <div aria-live="polite" className="km-semantic-merge-status" role="alert">
            <p>{t(capabilityError
              ? `semanticExplore.query.error.${capabilityError}`
              : 'semanticMerge.error.generic')}</p>
            <button onClick={() => void onRefreshCapabilities()} type="button">
              {t('semanticMerge.retry')}
            </button>
          </div>
          </>
        ) : (
          <div className="km-semantic-merge-status">
            <LoadingProgress label={t('semanticMerge.capabilities.loading')} />
          </div>
        )}
      </section>
    );
  }

  return (
    <SemanticMergeReadyRuntime
      authoringContextRevision={authoringContextRevision}
      bridge={bridge}
      canImportChangeSet={canImportChangeSet}
      canNavigateRecord={canNavigateRecord}
      changeSets={changeSets}
      expectedChangeSetETag={expectedChangeSetETag}
      isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
      isChangeSetWorkspaceReady={isChangeSetWorkspaceReady}
      onImportRecipe={onImportRecipe}
      onImportSemanticMerge={onImportSemanticMerge}
      onNavigateRecord={onNavigateRecord}
      onOpenChanges={onOpenChanges}
      onPickSource={onPickSource}
      onPreparationStateChange={onPreparationStateChange}
      onStaleRevision={onStaleRevision}
      revision={revision}
      scope={scope}
    />
  );
}

function SemanticMergeReadyRuntime({
  authoringContextRevision,
  bridge,
  canImportChangeSet,
  canNavigateRecord,
  changeSets,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onImportRecipe,
  onImportSemanticMerge,
  onNavigateRecord,
  onOpenChanges,
  onPickSource,
  onPreparationStateChange,
  onStaleRevision,
  revision,
  scope
}: Omit<
  SemanticMergeRuntimeProps,
  | 'capabilityError'
  | 'capabilityStatus'
  | 'onEnsureCapabilities'
  | 'onRefreshCapabilities'
  | 'revision'
> & { revision: SemanticExploreRevision }) {
  const controller = useSemanticMergeController({
    authoringContextRevision,
    bridge,
    expectedChangeSetETag,
    isAuthoringContextReady: isChangeSetWorkspaceReady,
    onStaleRevision,
    revision,
    scope
  });

  useEffect(() => {
    if (controller.capabilities.status === 'idle') {
      void controller.ensureCapabilities();
    }
  }, [
    authoringContextRevision,
    bridge,
    controller.capabilities.status,
    controller.ensureCapabilities,
    expectedChangeSetETag,
    revision,
    scope
  ]);
  useEffect(() => {
    onPreparationStateChange?.(
      preparationProgressFromQueryStatuses('semanticMerge', [controller.capabilities.status])
    );
  }, [controller.capabilities.status, onPreparationStateChange]);

  return (
    <SemanticMergeSection
      authoringContextRevision={authoringContextRevision}
      canImportChangeSet={canImportChangeSet}
      canNavigateRecord={canNavigateRecord}
      changeSets={changeSets}
      controller={controller}
      expectedChangeSetETag={expectedChangeSetETag}
      isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
      isChangeSetWorkspaceReady={isChangeSetWorkspaceReady}
      onImportRecipe={onImportRecipe}
      onImportSemanticMerge={onImportSemanticMerge}
      onNavigateRecord={onNavigateRecord}
      onOpenChanges={onOpenChanges}
      onPickSource={onPickSource}
      revision={revision}
      scope={scope}
    />
  );
}
