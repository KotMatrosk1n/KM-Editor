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
import { useLocalization } from '../../localization';
import type { SemanticQueryStatus } from '../semantic-explore/useSemanticExploreController';
import type { ExpectedImportedScalarEdit } from '../change-sets/useChangeSetWorkspaceController';
import { SemanticMergeSection } from './SemanticMergeSection';
import { useSemanticMergeController } from './useSemanticMergeController';

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
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export function SemanticMergeRuntime({
  authoringContextRevision,
  bridge,
  canImportChangeSet,
  canNavigateRecord,
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
  onStaleRevision,
  revision,
  scope
}: SemanticMergeRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') void onEnsureCapabilities();
  }, [capabilityStatus, onEnsureCapabilities, revision]);

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
        <div
          aria-live="polite"
          className="km-semantic-merge-status"
          role={isError ? 'alert' : 'status'}
        >
          <p>{t(isError
            ? 'semanticMerge.error.generic'
            : 'semanticMerge.capabilities.loading')}</p>
          {isError ? (
            <button onClick={() => void onEnsureCapabilities()} type="button">
              {t('semanticMerge.retry')}
            </button>
          ) : null}
        </div>
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
  onStaleRevision,
  revision,
  scope
}: Omit<
  SemanticMergeRuntimeProps,
  'capabilityStatus' | 'onEnsureCapabilities' | 'revision'
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
    void controller.ensureCapabilities();
  }, [controller.ensureCapabilities]);

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
