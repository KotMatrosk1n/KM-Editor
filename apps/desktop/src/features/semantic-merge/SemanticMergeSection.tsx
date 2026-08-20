/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Check,
  ChevronDown,
  Clipboard,
  Download,
  FileJson,
  FolderOpen,
  GitMerge,
  Search,
  ShieldAlert,
  Trash2
} from 'lucide-react';
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type FormEvent
} from 'react';
import type { ApiDiagnostic } from '../../bridge/contracts';
import {
  kmRecipeMaximumBytes,
  kmRecipeMaximumOperations,
  kmRecipeMaximumSteps,
  semanticMergeContractKeys,
  semanticMergeMaximumChangeSetNameLength,
  semanticMergeMaximumTargetSearchTextLength,
  semanticMergeMaximumTargets,
  type KmRecipeArtifact,
  type KmRecipeImportRequest,
  type KmRecipeImportResponse,
  type SemanticMergeCapability,
  type SemanticMergeConflictChoice,
  type SemanticMergeConflictResolution,
  type SemanticMergeFieldRef,
  type SemanticMergeImportRequest,
  type SemanticMergeImportResponse,
  type SemanticMergeRow
} from '../../bridge/semanticMergeContracts';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';
import type { ExpectedImportedScalarEdit } from '../change-sets/useChangeSetWorkspaceController';
import type { SemanticMergeChangeSetOption } from './SemanticMergeRuntime';
import type {
  SemanticMergeController,
  SemanticMergeQueryError
} from './useSemanticMergeController';
import './semanticMerge.css';

export type SemanticMergeSectionProps = {
  authoringContextRevision: string | null;
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  changeSets: readonly SemanticMergeChangeSetOption[];
  controller: SemanticMergeController;
  expectedChangeSetETag: string | null;
  isChangeSetWorkspaceBusy: boolean;
  isChangeSetWorkspaceReady: boolean;
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
  revision: SemanticExploreRevision;
  scope: SemanticExploreScope;
};

type ActiveSurface = 'merge' | 'recipes';
type ImportReceipt = { kind: 'merge' | 'recipe' };

const featureOrder = [
  'threeWayScalarMerge',
  'focusedConflictResolution',
  'stableCollectionMerge',
  'opaqueFileFallback',
  'recipeImport',
  'recipeExport',
  'compatibilityReport',
  'seededReproducibility',
  'headlessAutomation'
] as const;

export function SemanticMergeSection({
  authoringContextRevision,
  canImportChangeSet,
  canNavigateRecord,
  changeSets,
  controller,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onImportRecipe,
  onImportSemanticMerge,
  onNavigateRecord,
  onOpenChanges,
  onPickSource,
  revision,
  scope
}: SemanticMergeSectionProps) {
  const { t } = useLocalization();
  const [activeSurface, setActiveSurface] = useState<ActiveSurface>('merge');
  const [receipt, setReceipt] = useState<ImportReceipt | null>(null);
  const receiptRef = useRef<HTMLDivElement | null>(null);
  const scopeIdentity = JSON.stringify([
    scope.projectId,
    scope.paths.selectedGame,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
  useEffect(() => setReceipt(null), [scopeIdentity]);
  useEffect(() => {
    if (receipt) receiptRef.current?.focus({ preventScroll: true });
  }, [receipt]);

  const capabilities = controller.capabilities.data?.capabilities ?? [];
  const capabilityMap = useMemo(() => new Map(
    capabilities.map((capability) => [capability.feature, capability])
  ), [capabilities]);

  return (
    <section
      aria-labelledby="semantic-merge-title"
      aria-busy={controller.isQuerying || isChangeSetWorkspaceBusy}
      className="km-semantic-merge wide-panel"
    >
      <header className="km-semantic-merge-heading">
        <GitMerge aria-hidden="true" size={22} />
        <div>
          <p>{t('semanticMerge.eyebrow')}</p>
          <h2 id="semantic-merge-title">{t('semanticMerge.title')}</h2>
          <span>{t('semanticMerge.description')}</span>
        </div>
      </header>

      <section aria-labelledby="semantic-merge-capabilities-title">
        <div className="km-semantic-merge-section-heading">
          <h3 id="semantic-merge-capabilities-title">
            {t('semanticMerge.capabilities.title')}
          </h3>
          {controller.capabilities.status === 'loading' ? (
            <span role="status">{t('semanticMerge.capabilities.loading')}</span>
          ) : null}
        </div>
        {controller.capabilities.error ? (
          <QueryError error={controller.capabilities.error} onRetry={controller.ensureCapabilities} />
        ) : null}
        <div className="km-semantic-merge-capabilities">
          {featureOrder.map((feature) => (
            <CapabilityCard
              capability={capabilityMap.get(feature) ?? null}
              feature={feature}
              key={feature}
            />
          ))}
        </div>
        <p className="km-semantic-merge-boundary">
          <ShieldAlert aria-hidden="true" size={16} />
          <span>{t('semanticMerge.boundary')}</span>
        </p>
      </section>

      <div aria-label={t('semanticMerge.surface.label')} className="km-semantic-merge-tabs">
        <button
          aria-pressed={activeSurface === 'merge'}
          onClick={() => setActiveSurface('merge')}
          type="button"
        >
          {t('semanticMerge.merge.tab')}
        </button>
        <button
          aria-pressed={activeSurface === 'recipes'}
          onClick={() => setActiveSurface('recipes')}
          type="button"
        >
          {t('semanticMerge.recipes.tab')}
        </button>
      </div>

      {receipt ? (
        <div
          className="km-semantic-merge-receipt"
          ref={receiptRef}
          role="status"
          tabIndex={-1}
        >
          <Check aria-hidden="true" size={18} />
          <div>
            <strong>{t(`semanticMerge.import.${receipt.kind}.success`)}</strong>
            <p>{t('semanticMerge.import.success.description')}</p>
          </div>
          <button className="secondary-button" onClick={onOpenChanges} type="button">
            {t('semanticMerge.openChanges')}
          </button>
        </div>
      ) : null}

      {activeSurface === 'merge' ? (
        <MergeSurface
          authoringContextRevision={authoringContextRevision}
          canImportChangeSet={canImportChangeSet}
          canNavigateRecord={canNavigateRecord}
          capability={capabilityMap.get('threeWayScalarMerge') ?? null}
          controller={controller}
          expectedChangeSetETag={expectedChangeSetETag}
          isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
          isChangeSetWorkspaceReady={isChangeSetWorkspaceReady}
          onImported={() => setReceipt({ kind: 'merge' })}
          onImportSemanticMerge={onImportSemanticMerge}
          onNavigateRecord={onNavigateRecord}
          onPickSource={onPickSource}
          revision={revision}
          scope={scope}
        />
      ) : (
        <RecipeSurface
          authoringContextRevision={authoringContextRevision}
          canImportChangeSet={canImportChangeSet}
          canNavigateRecord={canNavigateRecord}
          changeSets={changeSets}
          controller={controller}
          expectedChangeSetETag={expectedChangeSetETag}
          importCapability={capabilityMap.get('recipeImport') ?? null}
          exportCapability={capabilityMap.get('recipeExport') ?? null}
          isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
          isChangeSetWorkspaceReady={isChangeSetWorkspaceReady}
          onImported={() => setReceipt({ kind: 'recipe' })}
          onImportRecipe={onImportRecipe}
          onNavigateRecord={onNavigateRecord}
          revision={revision}
          scope={scope}
        />
      )}
    </section>
  );
}

function CapabilityCard({
  capability,
  feature
}: {
  capability: SemanticMergeCapability | null;
  feature: (typeof featureOrder)[number];
}) {
  const { t } = useLocalization();
  const state = capability?.state ?? 'unavailable';
  return (
    <article className={`km-semantic-merge-capability is-${state}`}>
      <div>
        <strong>{t(`semanticMerge.feature.${feature}`)}</strong>
        <span>{t(`semanticMerge.state.${state}`)}</span>
      </div>
      <p>{capability?.reasonCode
        ? reasonText(capability.reasonCode, t)
        : t('semanticMerge.capabilities.noReason')}</p>
      {capability && capability.domains.length > 0 ? (
        <ul>
          {capability.domains.map((domain) => (
            <li data-localization-ignore="true" key={`${domain.domain}:${domain.recordKind}`}>
              {domain.domain} · {domain.recordKind} · {domain.fieldKeys.join(', ')}
            </li>
          ))}
        </ul>
      ) : null}
    </article>
  );
}

function MergeSurface({
  authoringContextRevision,
  canImportChangeSet,
  canNavigateRecord,
  capability,
  controller,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onImported,
  onImportSemanticMerge,
  onNavigateRecord,
  onPickSource,
  revision,
  scope
}: {
  authoringContextRevision: string | null;
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  capability: SemanticMergeCapability | null;
  controller: SemanticMergeController;
  expectedChangeSetETag: string | null;
  isChangeSetWorkspaceBusy: boolean;
  isChangeSetWorkspaceReady: boolean;
  onImported: () => void;
  onImportSemanticMerge: (
    request: SemanticMergeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => Promise<SemanticMergeImportResponse>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onPickSource: (slot: 'a' | 'b') => Promise<string | null>;
  revision: SemanticExploreRevision;
  scope: SemanticExploreScope;
}) {
  const { t } = useLocalization();
  const [searchDraft, setSearchDraft] = useState('');
  const [selectedRows, setSelectedRows] = useState<Map<string, SemanticMergeRow>>(new Map());
  const [resolutionDraft, setResolutionDraft] = useState<
    Map<string, SemanticMergeConflictChoice>
  >(new Map());
  const [changeSetName, setChangeSetName] = useState(t('semanticMerge.merge.defaultName'));
  const [isImporting, setIsImporting] = useState(false);
  const [importError, setImportError] = useState(false);
  const importStatusRef = useRef<HTMLDivElement | null>(null);
  const resultsHeadingRef = useRef<HTMLHeadingElement | null>(null);
  const isMountedRef = useRef(true);
  const pickerGenerationRef = useRef({ a: 0, b: 0 });
  const pickerContextIdentity = JSON.stringify([
    scope.projectId,
    revision.generation,
    revision.fingerprint,
    expectedChangeSetETag,
    authoringContextRevision
  ]);
  const pickerContextIdentityRef = useRef(pickerContextIdentity);
  pickerContextIdentityRef.current = pickerContextIdentity;
  const preview = controller.mergePreview.data;
  const isAvailable = capability?.state !== 'unavailable';
  const isBlocked = !isAvailable ||
    !isChangeSetWorkspaceReady ||
    isChangeSetWorkspaceBusy ||
    controller.isQuerying;
  const sourceIdentity = `${controller.sourceA.data?.instanceId ?? ''}:${controller.sourceB.data?.instanceId ?? ''}`;

  useEffect(() => {
    setSelectedRows(new Map());
    setResolutionDraft(new Map());
    setSearchDraft('');
    setImportError(false);
  }, [sourceIdentity]);
  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      pickerGenerationRef.current.a += 1;
      pickerGenerationRef.current.b += 1;
    };
  }, []);
  useEffect(() => {
    if (!preview || preview.selectionRequired) return;
    setImportError(false);
    setResolutionDraft(new Map(
      preview.normalizedResolutions.map((resolution) => [
        resolution.conflictId,
        resolution.choice
      ])
    ));
    resultsHeadingRef.current?.focus({ preventScroll: true });
  }, [preview?.proposalFingerprint]);
  useEffect(() => {
    if (importError) importStatusRef.current?.focus({ preventScroll: true });
  }, [importError]);

  const selectedTargets = [...selectedRows.values()].map((row) => row.target);
  const selectedDomain = selectedTargets[0]?.record.domain ?? null;
  const selectionMatchesProposal = Boolean(
    preview && !preview.selectionRequired &&
    sameFieldRefSet(selectedTargets, preview.normalizedTargets)
  );
  const draftResolutions = [...resolutionDraft].map(([conflictId, choice]) => ({
    choice,
    conflictId
  }));
  const resolutionsMatchProposal = Boolean(
    preview && sameResolutionSet(draftResolutions, preview.normalizedResolutions)
  );
  const completeReview = Boolean(preview && preview.nextCursor === null);
  const canImport = Boolean(
    preview &&
    !preview.selectionRequired &&
    preview.canImport &&
    completeReview &&
    selectionMatchesProposal &&
    resolutionsMatchProposal &&
    canImportChangeSet &&
    isChangeSetWorkspaceReady &&
    !isChangeSetWorkspaceBusy &&
    !isImporting &&
    changeSetName.trim().length > 0 &&
    changeSetName.trim().length <= semanticMergeMaximumChangeSetNameLength &&
    safeDiagnosticMessage(changeSetName.trim()) !== null
  );
  const normalizedSearch = normalizeSearch(searchDraft);
  const searchMatchesPreview = Boolean(
    preview?.selectionRequired && preview.normalizedTargetSearchText === normalizedSearch
  );

  const pickSource = async (slot: 'a' | 'b') => {
    const generation = ++pickerGenerationRef.current[slot];
    const requestedContextIdentity = pickerContextIdentityRef.current;
    const selectedPath = await onPickSource(slot);
    if (
      selectedPath === null ||
      !isMountedRef.current ||
      pickerGenerationRef.current[slot] !== generation ||
      pickerContextIdentityRef.current !== requestedContextIdentity
    ) return;
    await controller.openSource(slot, selectedPath);
  };
  const discover = async (event: FormEvent) => {
    event.preventDefault();
    setResolutionDraft(new Map());
    await controller.previewMerge([], [], normalizedSearch);
  };
  const toggleTarget = (row: SemanticMergeRow, checked: boolean) => {
    setSelectedRows((current) => {
      const next = new Map(current);
      const key = semanticMergeContractKeys.fieldRefKey(row.target);
      if (checked) {
        if (
          next.size >= semanticMergeMaximumTargets ||
          (selectedDomain !== null && selectedDomain !== row.target.record.domain)
        ) return current;
        next.set(key, row);
      } else {
        next.delete(key);
      }
      return next;
    });
  };
  const generate = async () => {
    if (selectedTargets.length === 0) return;
    setResolutionDraft(new Map());
    await controller.previewMerge(selectedTargets, [], null);
  };
  const updateChoices = async () => {
    if (!preview || preview.selectionRequired || !selectionMatchesProposal) return;
    await controller.previewMerge(preview.normalizedTargets, draftResolutions, null);
  };
  const importProposal = async () => {
    if (!canImport || !preview) return;
    const expectedEdits = preview.rows.flatMap((row): ExpectedImportedScalarEdit[] => {
      if (
        row.state !== 'autoMerged' ||
        row.resultValue?.canonicalValue == null ||
        !isCanonicalInt32(row.resultValue.canonicalValue) ||
        row.pendingValue == null ||
        (
          row.resultValue.kind === row.pendingValue.kind &&
          row.resultValue.canonicalValue === row.pendingValue.canonicalValue
        )
      ) return [];
      return [{
        domain: row.target.record.domain,
        field: row.target.fieldKey,
        newValue: row.resultValue.canonicalValue,
        recordId: row.target.record.recordId
      }];
    });
    if (expectedEdits.length !== preview.totalMutationCount) return;
    setIsImporting(true);
    setImportError(false);
    try {
      const response = await onImportSemanticMerge({
        changeSetName: changeSetName.trim(),
        expectedChangeSetETag,
        expectedRevision: revision,
        proposalFingerprint: preview.proposalFingerprint,
        proposalId: preview.proposalId,
        resolutions: preview.normalizedResolutions,
        scope,
        sourceAInstanceId: controller.sourceA.data!.instanceId,
        sourceBInstanceId: controller.sourceB.data!.instanceId,
        targets: preview.normalizedTargets
      }, expectedEdits);
      if (
        response.proposalId !== preview.proposalId ||
        response.proposalFingerprint !== preview.proposalFingerprint ||
        !sameRevision(response.revision, revision)
      ) throw new Error('Stale semantic merge import response.');
      onImported();
    } catch {
      setImportError(true);
    } finally {
      setIsImporting(false);
    }
  };

  return (
    <section aria-labelledby="semantic-merge-merge-title" className="km-semantic-merge-surface">
      <div className="km-semantic-merge-section-heading">
        <div>
          <h3 id="semantic-merge-merge-title">{t('semanticMerge.merge.title')}</h3>
          <p>{t('semanticMerge.merge.description')}</p>
        </div>
      </div>

      {!isAvailable ? (
        <UnavailablePanel reason={capability?.reasonCode ?? 'provider-fields-unavailable'} />
      ) : (
        <>
          <fieldset className="km-semantic-merge-fieldset">
            <legend>{t('semanticMerge.sources.legend')}</legend>
            <p>{t('semanticMerge.sources.description')}</p>
            <div className="km-semantic-merge-source-grid">
              <SourcePicker
                disabled={isBlocked || controller.sourceA.status === 'loading'}
                error={controller.sourceA.error}
                label={t('semanticMerge.source.a')}
                loaded={controller.sourceA.data !== null}
                loading={controller.sourceA.status === 'loading'}
                onClear={() => controller.clearSource('a')}
                onPick={() => void pickSource('a')}
              />
              <SourcePicker
                disabled={isBlocked || controller.sourceB.status === 'loading'}
                error={controller.sourceB.error}
                label={t('semanticMerge.source.b')}
                loaded={controller.sourceB.data !== null}
                loading={controller.sourceB.status === 'loading'}
                onClear={() => controller.clearSource('b')}
                onPick={() => void pickSource('b')}
              />
            </div>
          </fieldset>

          {controller.sourceA.data && controller.sourceB.data ? (
            <fieldset className="km-semantic-merge-fieldset">
              <legend>{t('semanticMerge.targets.legend')}</legend>
              <p>{t('semanticMerge.targets.singleDomain')}</p>
              <form className="km-semantic-merge-search" onSubmit={(event) => void discover(event)}>
                <label htmlFor="semantic-merge-target-search">
                  {t('semanticMerge.targets.search')}
                </label>
                <div>
                  <Search aria-hidden="true" size={16} />
                  <input
                    disabled={isBlocked || controller.mergePreview.status === 'loading'}
                    id="semantic-merge-target-search"
                    maxLength={semanticMergeMaximumTargetSearchTextLength}
                    onChange={(event) => setSearchDraft(event.target.value)}
                    value={searchDraft}
                  />
                  <button disabled={isBlocked} type="submit">
                    {t('semanticMerge.targets.find')}
                  </button>
                </div>
              </form>
              <SelectedTargets
                rows={[...selectedRows.values()]}
                onClear={() => setSelectedRows(new Map())}
                onRemove={(row) => toggleTarget(row, false)}
              />
              {selectedTargets.length > 0 ? (
                <button disabled={isBlocked} onClick={() => void generate()} type="button">
                  {t('semanticMerge.targets.generate')}
                </button>
              ) : null}
            </fieldset>
          ) : null}

          {controller.mergePreview.error ? (
            <QueryError
              error={controller.mergePreview.error}
              onRetry={preview?.selectionRequired ? () => controller.previewMerge([], [], normalizedSearch) : undefined}
            />
          ) : null}

          {preview?.selectionRequired && searchMatchesPreview ? (
            <TargetDiscovery
              disabled={isBlocked}
              onLoadMore={controller.loadMoreMerge}
              onToggle={toggleTarget}
              preview={preview}
              selectedDomain={selectedDomain}
              selectedKeys={new Set(selectedRows.keys())}
            />
          ) : null}

          {preview && !preview.selectionRequired && selectionMatchesProposal ? (
            <section aria-labelledby="semantic-merge-results-title" className="km-semantic-merge-results">
              <div className="km-semantic-merge-section-heading">
                <div>
                  <h3 id="semantic-merge-results-title" ref={resultsHeadingRef} tabIndex={-1}>
                    {t('semanticMerge.results.title')}
                  </h3>
                  <p>{t('semanticMerge.results.description')}</p>
                </div>
                <dl>
                  <div><dt>{t('semanticMerge.results.rows')}</dt><dd>{preview.totalRowCount}</dd></div>
                  <div><dt>{t('semanticMerge.results.conflicts')}</dt><dd>{preview.totalConflictCount}</dd></div>
                  <div><dt>{t('semanticMerge.results.mutations')}</dt><dd>{preview.totalMutationCount}</dd></div>
                </dl>
              </div>
              <MergeRows
                canNavigateRecord={canNavigateRecord}
                onChoice={(conflictId, choice) => setResolutionDraft((current) => {
                  const next = new Map(current);
                  next.set(conflictId, choice);
                  return next;
                })}
                onNavigateRecord={onNavigateRecord}
                resolutionDraft={resolutionDraft}
                rows={preview.rows}
              />
              {preview.nextCursor ? (
                <button
                  disabled={isBlocked || controller.mergePreview.isAppending}
                  onClick={() => void controller.loadMoreMerge()}
                  type="button"
                >
                  <ChevronDown aria-hidden="true" size={16} />
                  {t('semanticMerge.loadMore')}
                </button>
              ) : null}
              {!resolutionsMatchProposal ? (
                <div className="km-semantic-merge-review-needed">
                  <p>{t('semanticMerge.conflict.reviewChoices')}</p>
                  <button disabled={isBlocked} onClick={() => void updateChoices()} type="button">
                    {t('semanticMerge.conflict.updatePreview')}
                  </button>
                </div>
              ) : null}
              <Diagnostics diagnostics={preview.diagnostics} />
              <fieldset className="km-semantic-merge-fieldset">
                <legend>{t('semanticMerge.import.legend')}</legend>
                <p>{t('semanticMerge.import.disabledSet')}</p>
                <label htmlFor="semantic-merge-change-set-name">
                  {t('semanticMerge.import.name')}
                </label>
                <input
                  id="semantic-merge-change-set-name"
                  maxLength={semanticMergeMaximumChangeSetNameLength}
                  onChange={(event) => setChangeSetName(event.target.value)}
                  value={changeSetName}
                />
                {!completeReview ? <p>{t('semanticMerge.import.loadAll')}</p> : null}
                {!preview.canImport ? <p>{t('semanticMerge.import.blocked')}</p> : null}
                <button disabled={!canImport} onClick={() => void importProposal()} type="button">
                  {isImporting
                    ? t('semanticMerge.import.importing')
                    : t('semanticMerge.import.merge.action')}
                </button>
                {importError ? (
                  <div ref={importStatusRef} role="alert" tabIndex={-1}>
                    {t('semanticMerge.import.error')}
                  </div>
                ) : null}
              </fieldset>
            </section>
          ) : null}
        </>
      )}
    </section>
  );
}

function SourcePicker({
  disabled,
  error,
  label,
  loaded,
  loading,
  onClear,
  onPick
}: {
  disabled: boolean;
  error: SemanticMergeQueryError | null;
  label: string;
  loaded: boolean;
  loading: boolean;
  onClear: () => void;
  onPick: () => void;
}) {
  const { t } = useLocalization();
  return (
    <article aria-busy={loading} className="km-semantic-merge-source">
      <strong>{label}</strong>
      <span role="status">{t(loading
        ? 'semanticMerge.source.loading'
        : loaded
          ? 'semanticMerge.source.loaded'
          : 'semanticMerge.source.empty')}</span>
      <div>
        <button disabled={disabled} onClick={onPick} type="button">
          <FolderOpen aria-hidden="true" size={16} />
          {t(loaded ? 'semanticMerge.source.replace' : 'semanticMerge.source.choose')}
        </button>
        {loaded ? (
          <button aria-label={t('semanticMerge.source.clear')} onClick={onClear} type="button">
            <Trash2 aria-hidden="true" size={16} />
          </button>
        ) : null}
      </div>
      {error ? <QueryError error={error} /> : null}
    </article>
  );
}

function SelectedTargets({
  onClear,
  onRemove,
  rows
}: {
  onClear: () => void;
  onRemove: (row: SemanticMergeRow) => void;
  rows: readonly SemanticMergeRow[];
}) {
  const { t } = useLocalization();
  if (rows.length === 0) return <p>{t('semanticMerge.targets.none')}</p>;
  return (
    <div className="km-semantic-merge-selected">
      <div>
        <strong>{t('semanticMerge.targets.selected').replace('{count}', String(rows.length))}</strong>
        <button className="text-button" onClick={onClear} type="button">
          {t('semanticMerge.targets.clear')}
        </button>
      </div>
      <ul>
        {rows.map((row) => (
          <li key={semanticMergeContractKeys.fieldRefKey(row.target)}>
            <span data-localization-ignore="true">
              {row.recordLabel} · {row.fieldLabel} · {formatRecordRef(row.target.record)}
            </span>
            <button
              aria-label={t('semanticMerge.targets.remove')}
              onClick={() => onRemove(row)}
              type="button"
            >
              <Trash2 aria-hidden="true" size={14} />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

function TargetDiscovery({
  disabled,
  onLoadMore,
  onToggle,
  preview,
  selectedDomain,
  selectedKeys
}: {
  disabled: boolean;
  onLoadMore: () => Promise<void>;
  onToggle: (row: SemanticMergeRow, checked: boolean) => void;
  preview: NonNullable<SemanticMergeController['mergePreview']['data']>;
  selectedDomain: string | null;
  selectedKeys: ReadonlySet<string>;
}) {
  const { t } = useLocalization();
  return (
    <section aria-labelledby="semantic-merge-target-results" className="km-semantic-merge-discovery">
      <div className="km-semantic-merge-section-heading">
        <div>
          <h3 id="semantic-merge-target-results">{t('semanticMerge.targets.results')}</h3>
          <p>{t('semanticMerge.targets.matches')
            .replace('{loaded}', String(preview.rows.length))
            .replace('{total}', String(preview.totalMatchingTargetCount))}</p>
        </div>
      </div>
      {preview.targetWindowCapped ? <p>{t('semanticMerge.targets.windowCapped')}</p> : null}
      <ul className="km-semantic-merge-target-list">
        {preview.rows.map((row) => {
          const key = semanticMergeContractKeys.fieldRefKey(row.target);
          const checked = selectedKeys.has(key);
          const wrongDomain = selectedDomain !== null && selectedDomain !== row.target.record.domain;
          return (
            <li key={row.rowId}>
              <label>
                <input
                  checked={checked}
                  disabled={disabled || (!checked && (wrongDomain || selectedKeys.size >= semanticMergeMaximumTargets))}
                  onChange={(event) => onToggle(row, event.target.checked)}
                  type="checkbox"
                />
                <span data-localization-ignore="true">
                  <strong>{row.recordLabel}</strong>
                  {row.fieldLabel} · {formatRecordRef(row.target.record)}
                </span>
              </label>
              {wrongDomain ? <small>{t('semanticMerge.targets.otherDomain')}</small> : null}
            </li>
          );
        })}
      </ul>
      {preview.nextCursor ? (
        <button disabled={disabled} onClick={() => void onLoadMore()} type="button">
          <ChevronDown aria-hidden="true" size={16} />
          {t('semanticMerge.loadMore')}
        </button>
      ) : null}
    </section>
  );
}

function MergeRows({
  canNavigateRecord,
  onChoice,
  onNavigateRecord,
  resolutionDraft,
  rows
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  onChoice: (conflictId: string, choice: SemanticMergeConflictChoice) => void;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  resolutionDraft: ReadonlyMap<string, SemanticMergeConflictChoice>;
  rows: readonly SemanticMergeRow[];
}) {
  const { t } = useLocalization();
  return (
    <div className="km-semantic-merge-rows">
      {rows.map((row) => (
        <article className={`km-semantic-merge-row is-${row.state}`} key={row.rowId}>
          <header>
            <div data-localization-ignore="true">
              <strong>{row.recordLabel}</strong>
              <span>{row.fieldLabel} · {formatRecordRef(row.target.record)}</span>
            </div>
            <div>
              <span>{t(`semanticMerge.row.state.${row.state}`)}</span>
              <button
                disabled={!canNavigateRecord(row.target.record)}
                onClick={() => onNavigateRecord(row.target.record)}
                type="button"
              >
                {t('semanticMerge.row.open')}
              </button>
            </div>
          </header>
          <dl className="km-semantic-merge-values">
            <ScalarValue label={t('semanticMerge.value.base')} value={row.baseValue?.displayValue} />
            <ScalarValue label={t('semanticMerge.value.modA')} value={row.sourceAValue?.displayValue} />
            <ScalarValue label={t('semanticMerge.value.modB')} value={row.sourceBValue?.displayValue} />
            <ScalarValue label={t('semanticMerge.value.layered')} value={row.currentValue?.displayValue} />
            <ScalarValue label={t('semanticMerge.value.pending')} value={row.pendingValue?.displayValue} />
            <ScalarValue label={t('semanticMerge.value.result')} value={row.resultValue?.displayValue} />
          </dl>
          {row.conflicts.map((conflict) => (
            <fieldset className="km-semantic-merge-conflict" key={conflict.conflictId}>
              <legend>{t(`semanticMerge.conflict.${conflict.kind}`)}</legend>
              <p>{reasonText(conflict.reasonCode, t)}</p>
              {conflict.allowedChoices.length === 0 ? (
                <p>{t('semanticMerge.conflict.noResolution')}</p>
              ) : (
                <div>
                  {conflict.allowedChoices.map((choice) => (
                    <label key={choice}>
                      <input
                        checked={(resolutionDraft.get(conflict.conflictId) ?? conflict.selectedChoice) === choice}
                        name={`semantic-merge-conflict-${conflict.conflictId}`}
                        onChange={() => onChoice(conflict.conflictId, choice)}
                        type="radio"
                      />
                      {t(`semanticMerge.choice.${choice}`)}
                    </label>
                  ))}
                </div>
              )}
            </fieldset>
          ))}
          <p className="km-semantic-merge-coverage">
            {t('semanticMerge.row.coverage')}: {t(`semanticMerge.state.${row.coverage}`)} ·{' '}
            {t(`semanticMerge.confidence.${row.confidence}`)}
          </p>
          {row.fallback.kind === 'unavailable' ? (
            <p>{reasonText(row.fallback.reasonCode, t)}</p>
          ) : null}
        </article>
      ))}
    </div>
  );
}

function RecipeSurface({
  authoringContextRevision,
  canImportChangeSet,
  canNavigateRecord,
  changeSets,
  controller,
  expectedChangeSetETag,
  exportCapability,
  importCapability,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onImported,
  onImportRecipe,
  onNavigateRecord,
  revision,
  scope
}: {
  authoringContextRevision: string | null;
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  changeSets: readonly SemanticMergeChangeSetOption[];
  controller: SemanticMergeController;
  expectedChangeSetETag: string | null;
  exportCapability: SemanticMergeCapability | null;
  importCapability: SemanticMergeCapability | null;
  isChangeSetWorkspaceBusy: boolean;
  isChangeSetWorkspaceReady: boolean;
  onImported: () => void;
  onImportRecipe: (
    request: KmRecipeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => Promise<KmRecipeImportResponse>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  revision: SemanticExploreRevision;
  scope: SemanticExploreScope;
}) {
  const { t } = useLocalization();
  const [selectedRootIds, setSelectedRootIds] = useState<Set<string>>(new Set());
  const [exportName, setExportName] = useState(t('semanticMerge.recipes.export.defaultName'));
  const [fileError, setFileError] = useState(false);
  const [recipeChangeSetName, setRecipeChangeSetName] = useState('');
  const [isImporting, setIsImporting] = useState(false);
  const [importError, setImportError] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const isMountedRef = useRef(true);
  const fileReadGenerationRef = useRef(0);
  const fileReadContextIdentity = JSON.stringify([
    scope.projectId,
    revision.generation,
    revision.fingerprint,
    expectedChangeSetETag,
    authoringContextRevision
  ]);
  const fileReadContextIdentityRef = useRef(fileReadContextIdentity);
  fileReadContextIdentityRef.current = fileReadContextIdentity;
  const recipeResultsRef = useRef<HTMLHeadingElement | null>(null);
  const importStatusRef = useRef<HTMLDivElement | null>(null);
  const validation = controller.recipeValidation.data;
  const preview = controller.recipePreview.data;
  const exportReady = exportCapability?.state !== 'unavailable';
  const importReady = importCapability?.state !== 'unavailable';
  const availableSets = changeSets.filter((changeSet) => !changeSet.archived);
  const selectedClosure = useMemo(
    () => resolveChangeSetClosure(selectedRootIds, changeSets, exportCapability),
    [changeSets, exportCapability, selectedRootIds]
  );

  useEffect(() => {
    setSelectedRootIds((current) => new Set(
      [...current].filter((id) => availableSets.some((changeSet) => (
        changeSet.changeSetId === id &&
        recipeExportEligibility(changeSet, exportCapability) === 'eligible'
      )))
    ));
  }, [changeSets, exportCapability]);
  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      fileReadGenerationRef.current += 1;
    };
  }, []);
  useEffect(() => {
    if (!validation) return;
    setRecipeChangeSetName(validation.metadata.name.slice(0, semanticMergeMaximumChangeSetNameLength));
  }, [validation?.recipeFingerprint]);
  useEffect(() => {
    if (preview) recipeResultsRef.current?.focus({ preventScroll: true });
  }, [preview?.proposalFingerprint]);
  useEffect(() => {
    if (importError || fileError) importStatusRef.current?.focus({ preventScroll: true });
  }, [fileError, importError]);

  const busy = isChangeSetWorkspaceBusy || controller.isQuerying;
  const canExport = Boolean(
    exportReady &&
    isChangeSetWorkspaceReady &&
    !busy &&
    expectedChangeSetETag &&
    selectedClosure.valid &&
    selectedClosure.ids.length > 0 &&
    selectedClosure.ids.length <= kmRecipeMaximumSteps &&
    exportName.trim().length > 0 &&
    exportName.trim().length <= semanticMergeMaximumChangeSetNameLength &&
    safeDiagnosticMessage(exportName.trim()) !== null
  );
  const canImport = Boolean(
    importReady &&
    preview?.canImport &&
    preview.nextCursor === null &&
    canImportChangeSet &&
    isChangeSetWorkspaceReady &&
    !isChangeSetWorkspaceBusy &&
    !isImporting &&
    recipeChangeSetName.trim().length > 0 &&
    recipeChangeSetName.trim().length <= semanticMergeMaximumChangeSetNameLength &&
    safeDiagnosticMessage(recipeChangeSetName.trim()) !== null
  );

  const exportRecipe = async () => {
    if (!canExport) return;
    await controller.exportSelectedRecipe({
      name: exportName.trim(),
      notes: null,
      seed: null,
      selectedChangeSetIds: selectedClosure.ids
    });
  };
  const readRecipe = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    const generation = ++fileReadGenerationRef.current;
    const requestedContextIdentity = fileReadContextIdentityRef.current;
    setFileError(false);
    setImportError(false);
    controller.clearRecipe();
    if (file.size === 0 || file.size > kmRecipeMaximumBytes) {
      setFileError(true);
      return;
    }
    try {
      const content = await file.text();
      if (
        !isMountedRef.current ||
        fileReadGenerationRef.current !== generation ||
        fileReadContextIdentityRef.current !== requestedContextIdentity
      ) return;
      if (new TextEncoder().encode(content).byteLength > kmRecipeMaximumBytes) {
        setFileError(true);
        return;
      }
      await controller.validateRecipe(content);
    } catch {
      setFileError(true);
    }
  };
  const importRecipe = async () => {
    if (!canImport || !preview || !validation) return;
    const expectedEdits = preview.compatibility.flatMap(
      (row): ExpectedImportedScalarEdit[] => row.state !== 'compatible'
        ? []
        : [{
            domain: row.target.record.domain,
            field: row.target.fieldKey,
            newValue: row.afterValue.canonicalValue,
            recordId: row.target.record.recordId
          }]
    );
    if (expectedEdits.length !== preview.totalMutationCount) return;
    setIsImporting(true);
    setImportError(false);
    try {
      const response = await onImportRecipe({
        changeSetName: recipeChangeSetName.trim(),
        expectedChangeSetETag,
        expectedRevision: revision,
        proposalFingerprint: preview.proposalFingerprint,
        proposalId: preview.proposalId,
        recipeFingerprint: validation.recipeFingerprint,
        recipeInstanceId: validation.recipeInstanceId,
        scope
      }, expectedEdits);
      if (
        response.recipeInstanceId !== validation.recipeInstanceId ||
        response.recipeFingerprint !== validation.recipeFingerprint ||
        response.proposalId !== preview.proposalId ||
        response.proposalFingerprint !== preview.proposalFingerprint ||
        !sameRevision(response.revision, revision)
      ) throw new Error('Stale recipe import response.');
      onImported();
    } catch {
      setImportError(true);
    } finally {
      setIsImporting(false);
    }
  };

  return (
    <section aria-labelledby="semantic-merge-recipes-title" className="km-semantic-merge-surface">
      <div className="km-semantic-merge-section-heading">
        <div>
          <h3 id="semantic-merge-recipes-title">{t('semanticMerge.recipes.title')}</h3>
          <p>{t('semanticMerge.recipes.description')}</p>
        </div>
      </div>

      <div className="km-semantic-merge-recipe-grid">
        <fieldset className="km-semantic-merge-fieldset">
          <legend>{t('semanticMerge.recipes.export.legend')}</legend>
          <p>{t('semanticMerge.recipes.export.description')}</p>
          {!exportReady ? (
            <UnavailablePanel reason={exportCapability?.reasonCode ?? 'provider-fields-unavailable'} />
          ) : (
            <>
              <label htmlFor="semantic-merge-recipe-name">
                {t('semanticMerge.recipes.export.name')}
              </label>
              <input
                id="semantic-merge-recipe-name"
                maxLength={semanticMergeMaximumChangeSetNameLength}
                onChange={(event) => setExportName(event.target.value)}
                value={exportName}
              />
              <div className="km-semantic-merge-change-sets">
                {availableSets.map((changeSet) => (
                  <label key={changeSet.changeSetId}>
                    <input
                      checked={selectedRootIds.has(changeSet.changeSetId)}
                      disabled={busy || recipeExportEligibility(
                        changeSet,
                        exportCapability
                      ) !== 'eligible'}
                      onChange={(event) => setSelectedRootIds((current) => {
                        const next = new Set(current);
                        if (event.target.checked) next.add(changeSet.changeSetId);
                        else next.delete(changeSet.changeSetId);
                        return next;
                      })}
                      type="checkbox"
                    />
                    <span data-localization-ignore="true">{changeSet.name}</span>
                    <small>{t('semanticMerge.recipes.export.operations')
                      .replace('{count}', String(changeSet.operationCount))}</small>
                    <small>{t(recipeExportEligibility(changeSet, exportCapability) === 'eligible'
                      ? 'semanticMerge.recipes.export.eligible'
                      : `semanticMerge.recipes.export.ineligible.${recipeExportEligibility(
                          changeSet,
                          exportCapability
                        )}`)}</small>
                  </label>
                ))}
              </div>
              <p>{t('semanticMerge.recipes.export.closure')
                .replace('{count}', String(selectedClosure.ids.length))}</p>
              {!selectedClosure.valid || selectedClosure.ids.length > kmRecipeMaximumSteps ? (
                <p>{t('semanticMerge.recipes.export.invalidClosure')}</p>
              ) : null}
              <p>{t('semanticMerge.recipes.seedUnavailable')}</p>
              <button disabled={!canExport} onClick={() => void exportRecipe()} type="button">
                <FileJson aria-hidden="true" size={16} />
                {t('semanticMerge.recipes.export.action')}
              </button>
            </>
          )}
          {controller.exportRecipe.error ? <QueryError error={controller.exportRecipe.error} /> : null}
          {controller.exportRecipe.data ? (
            <RecipeArtifactCard
              key={controller.exportRecipe.data.recipeFingerprint}
              artifact={controller.exportRecipe.data.artifact}
              operationCount={controller.exportRecipe.data.totalOperationCount}
            />
          ) : null}
        </fieldset>

        <fieldset className="km-semantic-merge-fieldset">
          <legend>{t('semanticMerge.recipes.import.legend')}</legend>
          <p>{t('semanticMerge.recipes.import.description')}</p>
          {!importReady ? (
            <UnavailablePanel reason={importCapability?.reasonCode ?? 'provider-fields-unavailable'} />
          ) : (
            <>
              <input
                accept=".kmrecipe,application/json"
                className="km-semantic-merge-file-input"
                onChange={(event) => void readRecipe(event)}
                ref={inputRef}
                type="file"
              />
              <button disabled={busy} onClick={() => inputRef.current?.click()} type="button">
                <FolderOpen aria-hidden="true" size={16} />
                {t('semanticMerge.recipes.import.choose')}
              </button>
              {validation ? (
                <div className="km-semantic-merge-recipe-summary">
                  <strong data-localization-ignore="true">{validation.metadata.name}</strong>
                  <p>{t('semanticMerge.recipes.import.summary')
                    .replace('{steps}', String(validation.totalStepCount))
                    .replace('{operations}', String(validation.totalOperationCount))}</p>
                  <p>{t(`semanticMerge.game.${validation.game}`)}</p>
                  {validation.game !== scope.paths.selectedGame ? (
                    <p>{t('semanticMerge.recipes.import.gameMismatch')}</p>
                  ) : null}
                  <button
                    disabled={busy || validation.game !== scope.paths.selectedGame}
                    onClick={() => void controller.previewRecipe()}
                    type="button"
                  >
                    {t('semanticMerge.recipes.import.preview')}
                  </button>
                </div>
              ) : (
                <p>{t('semanticMerge.recipes.import.none')}</p>
              )}
            </>
          )}
          {controller.recipeValidation.error ? (
            <QueryError error={controller.recipeValidation.error} />
          ) : null}
          {fileError ? (
            <div ref={importStatusRef} role="alert" tabIndex={-1}>
              {t('semanticMerge.recipes.import.fileError')}
            </div>
          ) : null}
        </fieldset>
      </div>

      {preview ? (
        <section aria-labelledby="semantic-merge-recipe-results" className="km-semantic-merge-results">
          <div className="km-semantic-merge-section-heading">
            <div>
              <h3 id="semantic-merge-recipe-results" ref={recipeResultsRef} tabIndex={-1}>
                {t('semanticMerge.recipes.compatibility.title')}
              </h3>
              <p>{t('semanticMerge.recipes.compatibility.description')}</p>
            </div>
            <dl>
              <div><dt>{t('semanticMerge.results.rows')}</dt><dd>{preview.totalCompatibilityCount}</dd></div>
              <div><dt>{t('semanticMerge.results.mutations')}</dt><dd>{preview.totalMutationCount}</dd></div>
            </dl>
          </div>
          <div className="km-semantic-merge-rows">
            {preview.compatibility.map((row) => (
              <article className={`km-semantic-merge-row is-${row.state}`} key={row.rowId}>
                <header>
                  <div data-localization-ignore="true">
                    <strong>{formatRecordRef(row.target.record)}</strong>
                    <span>{row.target.fieldKey}</span>
                  </div>
                  <div>
                    <span>{t(`semanticMerge.recipe.state.${row.state}`)}</span>
                    <button
                      disabled={!canNavigateRecord(row.target.record)}
                      onClick={() => onNavigateRecord(row.target.record)}
                      type="button"
                    >
                      {t('semanticMerge.row.open')}
                    </button>
                  </div>
                </header>
                <dl className="km-semantic-merge-values">
                  <ScalarValue label={t('semanticMerge.value.expectedBase')} value={row.expectedBaseValue.canonicalValue} />
                  <ScalarValue label={t('semanticMerge.value.actualBase')} value={row.actualBaseValue?.displayValue} />
                  <ScalarValue label={t('semanticMerge.value.expectedCurrent')} value={row.expectedCurrentValue.canonicalValue} />
                  <ScalarValue label={t('semanticMerge.value.layered')} value={row.currentValue?.displayValue} />
                  <ScalarValue label={t('semanticMerge.value.pending')} value={row.pendingValue?.displayValue} />
                  <ScalarValue label={t('semanticMerge.value.result')} value={row.afterValue.canonicalValue} />
                </dl>
                {row.reasonCode ? <p>{reasonText(row.reasonCode, t)}</p> : null}
              </article>
            ))}
          </div>
          {preview.nextCursor ? (
            <button disabled={busy} onClick={() => void controller.loadMoreRecipe()} type="button">
              <ChevronDown aria-hidden="true" size={16} />
              {t('semanticMerge.loadMore')}
            </button>
          ) : null}
          <Diagnostics diagnostics={preview.diagnostics} />
          <fieldset className="km-semantic-merge-fieldset">
            <legend>{t('semanticMerge.import.legend')}</legend>
            <p>{t('semanticMerge.import.disabledSet')}</p>
            <label htmlFor="semantic-merge-recipe-change-set-name">
              {t('semanticMerge.import.name')}
            </label>
            <input
              id="semantic-merge-recipe-change-set-name"
              maxLength={semanticMergeMaximumChangeSetNameLength}
              onChange={(event) => setRecipeChangeSetName(event.target.value)}
              value={recipeChangeSetName}
            />
            {preview.nextCursor ? <p>{t('semanticMerge.import.loadAll')}</p> : null}
            {!preview.canImport ? <p>{t('semanticMerge.import.blocked')}</p> : null}
            <button disabled={!canImport} onClick={() => void importRecipe()} type="button">
              {isImporting
                ? t('semanticMerge.import.importing')
                : t('semanticMerge.import.recipe.action')}
            </button>
            {importError ? (
              <div ref={importStatusRef} role="alert" tabIndex={-1}>
                {t('semanticMerge.import.error')}
              </div>
            ) : null}
          </fieldset>
        </section>
      ) : null}
      {controller.recipePreview.error ? <QueryError error={controller.recipePreview.error} /> : null}
    </section>
  );
}

function RecipeArtifactCard({
  artifact,
  operationCount
}: {
  artifact: KmRecipeArtifact;
  operationCount: number;
}) {
  const { t } = useLocalization();
  const [status, setStatus] = useState<'idle' | 'copied' | 'downloaded' | 'error'>('idle');
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(artifact.content);
      setStatus('copied');
    } catch {
      setStatus('error');
    }
  };
  const download = () => {
    try {
      const url = URL.createObjectURL(new Blob([artifact.content], { type: artifact.mediaType }));
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = artifact.suggestedFileName;
      anchor.click();
      URL.revokeObjectURL(url);
      setStatus('downloaded');
    } catch {
      setStatus('error');
    }
  };
  return (
    <div className="km-semantic-merge-artifact">
      <strong>{t('semanticMerge.recipes.export.ready')}</strong>
      <p>{t('semanticMerge.recipes.export.readyDescription')
        .replace('{count}', String(operationCount))}</p>
      <div>
        <button onClick={() => void copy()} type="button">
          <Clipboard aria-hidden="true" size={16} />
          {t('semanticMerge.recipes.export.copy')}
        </button>
        <button onClick={download} type="button">
          <Download aria-hidden="true" size={16} />
          {t('semanticMerge.recipes.export.download')}
        </button>
      </div>
      {status !== 'idle' ? (
        <span aria-live="polite" role={status === 'error' ? 'alert' : 'status'}>
          {t(`semanticMerge.recipes.export.${status}`)}
        </span>
      ) : null}
    </div>
  );
}

function ScalarValue({ label, value }: { label: string; value: string | undefined }) {
  const { t } = useLocalization();
  return (
    <div>
      <dt>{label}</dt>
      <dd data-localization-ignore="true">{value ?? t('semanticMerge.value.unavailable')}</dd>
    </div>
  );
}

function Diagnostics({ diagnostics }: { diagnostics: readonly ApiDiagnostic[] }) {
  const { t } = useLocalization();
  if (diagnostics.length === 0) return null;
  return (
    <section aria-label={t('semanticMerge.diagnostics.title')} className="km-semantic-merge-diagnostics">
      <h4>{t('semanticMerge.diagnostics.title')}</h4>
      <ul>
        {diagnostics.map((diagnostic, index) => (
          <li key={`${diagnostic.code ?? 'diagnostic'}:${index}`}>
            {diagnostic.code ? <code data-localization-ignore="true">{diagnostic.code}</code> : null}
            <span>{safeDiagnosticMessage(diagnostic.message) ?? t('semanticMerge.diagnostics.redacted')}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function QueryError({
  error,
  onRetry
}: {
  error: SemanticMergeQueryError;
  onRetry?: () => void | Promise<void>;
}) {
  const { t } = useLocalization();
  return (
    <div className="km-semantic-merge-error" role="alert">
      <p>{t(`semanticMerge.error.${error}`)}</p>
      {onRetry ? (
        <button onClick={() => void onRetry()} type="button">
          {t('semanticMerge.retry')}
        </button>
      ) : null}
    </div>
  );
}

function UnavailablePanel({ reason }: { reason: string }) {
  const { t } = useLocalization();
  return (
    <div className="km-semantic-merge-unavailable" role="status">
      <ShieldAlert aria-hidden="true" size={17} />
      <p>{reasonText(reason, t)}</p>
    </div>
  );
}

function normalizeSearch(value: string) {
  const normalized = value.trim().normalize('NFC');
  return normalized.length > 0 ? normalized : null;
}

function isCanonicalInt32(value: string) {
  if (!/^-?(?:0|[1-9][0-9]*)$/u.test(value) || value === '-0') return false;
  const parsed = Number(value);
  return Number.isInteger(parsed) &&
    parsed >= -2_147_483_648 &&
    parsed <= 2_147_483_647 &&
    String(parsed) === value;
}

function formatRecordRef(record: SemanticExploreRecordRef) {
  return [
    record.domain,
    `${record.recordKind.key}@${record.recordKind.schemaVersion}`,
    record.recordId,
    record.subrecordId
  ].filter((value) => value !== null).join(' · ');
}

function sameFieldRefSet(
  left: readonly SemanticMergeFieldRef[],
  right: readonly SemanticMergeFieldRef[]
) {
  return JSON.stringify(left.map(semanticMergeContractKeys.fieldRefKey).sort()) ===
    JSON.stringify(right.map(semanticMergeContractKeys.fieldRefKey).sort());
}

function sameResolutionSet(
  left: readonly SemanticMergeConflictResolution[],
  right: readonly SemanticMergeConflictResolution[]
) {
  const key = (value: SemanticMergeConflictResolution) => `${value.conflictId}:${value.choice}`;
  return JSON.stringify(left.map(key).sort()) === JSON.stringify(right.map(key).sort());
}

function sameRevision(left: SemanticExploreRevision, right: SemanticExploreRevision) {
  return left.projectId === right.projectId &&
    left.gameFamily === right.gameFamily &&
    left.generation === right.generation &&
    left.fingerprint === right.fingerprint;
}

function resolveChangeSetClosure(
  roots: ReadonlySet<string>,
  changeSets: readonly SemanticMergeChangeSetOption[],
  exportCapability: SemanticMergeCapability | null
) {
  const byId = new Map(changeSets.map((changeSet) => [changeSet.changeSetId, changeSet]));
  const orderById = new Map(changeSets.map((changeSet, index) => [changeSet.changeSetId, index]));
  const included = new Set<string>();
  const visiting = new Set<string>();
  let valid = true;
  const visit = (changeSetId: string) => {
    if (visiting.has(changeSetId)) {
      valid = false;
      return;
    }
    if (included.has(changeSetId)) return;
    const changeSet = byId.get(changeSetId);
    if (!changeSet) {
      valid = false;
      return;
    }
    if (recipeExportEligibility(changeSet, exportCapability) !== 'eligible') valid = false;
    visiting.add(changeSetId);
    changeSet.dependencyIds.forEach((dependencyId) => {
      if ((orderById.get(dependencyId) ?? Number.MAX_SAFE_INTEGER) >= (
        orderById.get(changeSetId) ?? -1
      )) valid = false;
      visit(dependencyId);
    });
    visiting.delete(changeSetId);
    included.add(changeSetId);
  };
  roots.forEach(visit);
  const selected = changeSets.filter((changeSet) => included.has(changeSet.changeSetId));
  const domains = new Set(selected.map((changeSet) => changeSet.recipeExportDomain));
  const operationCount = selected.reduce((count, changeSet) => count + changeSet.operationCount, 0);
  const targetKeys = selected.flatMap((changeSet) => changeSet.recipeExportTargetKeys);
  if (
    domains.size !== 1 || domains.has(null) ||
    operationCount < 1 || operationCount > kmRecipeMaximumOperations ||
    new Set(targetKeys).size !== targetKeys.length
  ) valid = false;
  return {
    ids: selected
      .map((changeSet) => changeSet.changeSetId),
    valid
  };
}

function recipeExportEligibility(
  changeSet: SemanticMergeChangeSetOption,
  exportCapability: SemanticMergeCapability | null
): SemanticMergeChangeSetOption['recipeExportEligibility'] | 'provider' {
  if (changeSet.recipeExportEligibility !== 'eligible') {
    return changeSet.recipeExportEligibility;
  }
  const domain = exportCapability?.domains.find((candidate) => (
    candidate.domain === changeSet.recipeExportDomain
  ));
  return domain && changeSet.recipeExportFieldKeys.every((fieldKey) => (
    domain.fieldKeys.includes(fieldKey)
  ))
    ? 'eligible'
    : 'provider';
}

function safeDiagnosticMessage(message: string) {
  const value = message.trim();
  if (!value || value.length > 8_192) return null;
  let candidate = value;
  for (let depth = 0; depth <= 3; depth += 1) {
    if (
      /[\\/]/u.test(candidate) ||
      /(?:^|[^A-Za-z0-9])[A-Za-z]:/u.test(candidate) ||
      /(?:^|[^A-Za-z0-9])file:/iu.test(candidate) ||
      /(?:^|[^A-Za-z0-9])~/u.test(candidate)
    ) return null;
    if (depth === 3 || !candidate.includes('%')) break;
    try {
      const decoded = decodeURIComponent(candidate);
      if (decoded === candidate) break;
      candidate = decoded;
    } catch {
      return null;
    }
  }
  return value;
}

function reasonText(reasonCode: string, t: (key: string) => string) {
  const knownReasons = new Set([
    'scalar-fields-single-domain-proposals-only',
    'focused-scalar-conflicts-only',
    'stable-collection-operation-provider-unavailable',
    'legacy-reviewed-transaction-boundary-unavailable',
    'seeded-recipe-provider-unavailable',
    'shared-cli-facade-unavailable',
    'legacy-review-transaction-hardening-required',
    'provider-fields-unavailable',
    'divergent-source-values',
    'current-layer-diverged-from-base',
    'pending-target-diverged-from-layered',
    'scalar-layout-mismatch',
    'semantic-owner-mismatch',
    'provider-validation-failed',
    'recipe-provider-field-unavailable',
    'recipe-base-preimage-mismatch',
    'recipe-current-preimage-mismatch',
    'recipe-pending-target-diverged',
    'recipe-scalar-value-unsupported',
    'recipe-provider-validation-failed'
  ]);
  return knownReasons.has(reasonCode)
    ? t(`semanticMerge.reason.${reasonCode}`)
    : t('semanticMerge.reason.unavailable');
}
