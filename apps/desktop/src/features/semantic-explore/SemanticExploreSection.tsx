/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ArrowRight,
  Boxes,
  FileDiff,
  FolderSearch,
  Network,
  Search
} from 'lucide-react';
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type ReactNode
} from 'react';
import type {
  SemanticExploreCoverage,
  SemanticExploreDifference,
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScalar,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import {
  PublishCommonEditorError,
  usePublishCommonEditorError
} from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';
import { semanticRecordRefKey } from '../../workbench/semanticContracts';
import { TechnicalDetails } from '../workbench/AnalysisPresentation';
import {
  humanizeIdentifier,
  presentFactValue
} from '../workbench/analysisPresentationUtils';
import {
  semanticExploreMaximumAccumulatedResults,
  type QueryableLayer,
  type SemanticExploreController,
  type SemanticQueryError,
  type SemanticQueryState
} from './useSemanticExploreController';
import './semanticExplore.css';

export type SemanticExploreSectionProps = {
  controller: SemanticExploreController;
  externalComparisonDisabled: boolean;
  onNavigateEntity: (record: SemanticExploreRecordRef) => void;
  onPickExternalMod: () => Promise<string | null>;
  scope: SemanticExploreScope;
};

type ExploreModule = 'explore' | 'compare' | 'ownership' | 'changes';

export function SemanticExploreSection({
  controller,
  externalComparisonDisabled,
  onNavigateEntity,
  onPickExternalMod,
  scope
}: SemanticExploreSectionProps) {
  const { t } = useLocalization();
  const features = useMemo(
    () => new Set(
      controller.capabilities.data?.providers.flatMap((provider) => provider.features) ?? []
    ),
    [controller.capabilities.data]
  );
  const modules = useMemo(() => [
    ...(features.has('search') || features.has('entity') ? ['explore' as const] : []),
    ...(features.has('compare') || features.has('externalCompare') ? ['compare' as const] : []),
    ...(features.has('ownership') ? ['ownership' as const] : []),
    ...(features.has('changes') ? ['changes' as const] : [])
  ], [features]);
  const [activeModule, setActiveModule] = useState<ExploreModule>('explore');

  useEffect(() => {
    if (controller.capabilities.status === 'idle') {
      void controller.ensureCapabilities();
    }
  }, [controller.capabilities.status, controller.ensureCapabilities, scope]);

  useEffect(() => {
    if (modules.length > 0 && !modules.includes(activeModule)) {
      setActiveModule(modules[0]!);
    }
  }, [activeModule, modules]);

  if (
    controller.capabilities.status === 'idle' ||
    (controller.capabilities.status === 'loading' && !controller.capabilities.data)
  ) {
    return <SemanticStatus kind="loading" />;
  }
  if (controller.capabilities.status === 'error') {
    return (
      <SemanticStatus
        error={controller.capabilities.error}
        kind="error"
        onRetry={() => void controller.refreshCapabilities()}
      />
    );
  }
  if (modules.length === 0) {
    const coverage = controller.capabilities.data?.providers.map(
      (provider) => provider.coverage
    ) ?? [];
    return (
      <section
        aria-busy={controller.isQuerying || undefined}
        aria-labelledby="semantic-explore-title"
        className="km-semantic-explore"
      >
        <SemanticHeading />
        {controller.capabilities.status === 'loading' ? (
          <LoadingProgress className="is-compact" label={t('semanticExplore.loading')} />
        ) : null}
        <p className="km-workbench-empty">{t('semanticExplore.unavailable')}</p>
        <CoverageSummary coverage={coverage} />
      </section>
    );
  }

  return (
    <section
      aria-busy={controller.isQuerying || undefined}
      aria-labelledby="semantic-explore-title"
      className="km-semantic-explore"
    >
      <SemanticHeading />
      {controller.capabilities.status === 'loading' ? (
        <LoadingProgress className="is-compact" label={t('semanticExplore.loading')} />
      ) : null}
      <div
        aria-label={t('semanticExplore.modules.label')}
        className="km-semantic-module-tabs"
        role="tablist"
      >
        {modules.map((module) => (
          <button
            aria-controls={`semantic-explore-module-panel-${module}`}
            aria-selected={activeModule === module}
            className={activeModule === module ? 'is-active' : undefined}
            id={`semantic-explore-module-${module}`}
            key={module}
            onClick={() => setActiveModule(module)}
            onKeyDown={(event) => {
              const nextIndex = moduleTabIndex(event.key, modules.indexOf(module), modules.length);
              if (nextIndex === null) return;
              event.preventDefault();
              const nextModule = modules[nextIndex];
              if (!nextModule) return;
              setActiveModule(nextModule);
              event.currentTarget.parentElement
                ?.querySelectorAll<HTMLButtonElement>('[role="tab"]')
                [nextIndex]?.focus({ preventScroll: true });
            }}
            role="tab"
            tabIndex={activeModule === module ? 0 : -1}
            type="button"
          >
            {t(`semanticExplore.module.${module}`)}
          </button>
        ))}
      </div>

      {modules.map((module) => (
        <div
          aria-labelledby={`semantic-explore-module-${module}`}
          className="km-semantic-module"
          hidden={activeModule !== module}
          id={`semantic-explore-module-panel-${module}`}
          key={module}
          role="tabpanel"
        >
          {module === 'explore' ? (
            <ExploreModulePanel controller={controller} onNavigateEntity={onNavigateEntity} />
          ) : null}
          {module === 'compare' ? (
            <CompareModulePanel
              controller={controller}
              externalComparisonDisabled={externalComparisonDisabled}
              onNavigateEntity={onNavigateEntity}
              onPickExternalMod={onPickExternalMod}
            />
          ) : null}
          {module === 'ownership' ? (
            <OwnershipModulePanel controller={controller} onNavigateEntity={onNavigateEntity} />
          ) : null}
          {module === 'changes' ? (
            <ChangesModulePanel controller={controller} onNavigateEntity={onNavigateEntity} />
          ) : null}
        </div>
      ))}
    </section>
  );
}

function SemanticHeading() {
  const { t } = useLocalization();
  return (
    <header className="km-semantic-heading">
      <div>
        <p>{t('semanticExplore.eyebrow')}</p>
        <h2 id="semantic-explore-title">{t('semanticExplore.title')}</h2>
        <span>{t('semanticExplore.description')}</span>
      </div>
    </header>
  );
}

function ExploreModulePanel({
  controller,
  onNavigateEntity
}: Pick<SemanticExploreSectionProps, 'controller' | 'onNavigateEntity'>) {
  const { t } = useLocalization();
  const [searchText, setSearchText] = useState('');
  const [layer, setLayer] = useState<QueryableLayer>(() => preferredLayer(controller));
  const layers = availableQueryableLayers(controller);
  const initializedRevisionRef = useRef<string | null>(null);
  useEffect(() => {
    const revision = controller.capabilities.data?.revision.fingerprint ?? null;
    if (revision && initializedRevisionRef.current !== revision) {
      initializedRevisionRef.current = revision;
      setLayer(preferredLayer(controller));
    } else if (!layers.includes(layer)) {
      setLayer(preferredLayer(controller));
    }
  }, [controller.capabilities.data?.revision.fingerprint, layer, layers]);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (searchText.trim().length === 0) return;
    void controller.searchEntities({ layer, searchText });
  };
  const selectEntity = (record: SemanticExploreRecordRef) => {
    void controller.getEntity(record, layer);
  };
  const searchItems = controller.search.data?.items ?? [];
  const searchIdentityCounts = new Map<string, number>();
  for (const item of searchItems) {
    const identity = JSON.stringify([
      item.displayName.trim().toLocaleLowerCase(),
      item.domainLabel.trim().toLocaleLowerCase(),
      item.description?.trim().toLocaleLowerCase() ?? null
    ]);
    searchIdentityCounts.set(identity, (searchIdentityCounts.get(identity) ?? 0) + 1);
  }
  const ambiguousSearchRecords = new Set(searchItems
    .filter((item) => searchIdentityCounts.get(JSON.stringify([
      item.displayName.trim().toLocaleLowerCase(),
      item.domainLabel.trim().toLocaleLowerCase(),
      item.description?.trim().toLocaleLowerCase() ?? null
    ]))! > 1)
    .map((item) => semanticRecordRefKey(item.record)));

  return (
    <div className="km-semantic-explore-grid">
      <div>
        <form className="km-semantic-search" onSubmit={submit} role="search">
          <label>
            <span>{t('semanticExplore.search.label')}</span>
            <span className="km-semantic-search-input">
              <Search aria-hidden="true" size={17} />
              <input
                autoComplete="off"
                maxLength={256}
                onChange={(event) => setSearchText(event.currentTarget.value)}
                placeholder={t('semanticExplore.search.placeholder')}
                type="search"
                value={searchText}
              />
            </span>
          </label>
          <LayerSelect layers={layers} onChange={setLayer} value={layer} />
          <button disabled={controller.search.status === 'loading'} type="submit">
            {t('semanticExplore.search.action')}
          </button>
        </form>

        <QueryBoundary state={controller.search}>
          {controller.search.data ? (
            <>
              <CoverageSummary coverage={controller.search.data.coverage} />
              {controller.search.data.items.length > 0 ? (
                <ul className="km-semantic-result-list">
                  {controller.search.data.items.map((item) => (
                    <li key={semanticRecordRefKey(item.record)}>
                      <button onClick={() => selectEntity(item.record)} type="button">
                        <span data-localization-ignore="true">
                          <strong>{item.displayName}</strong>
                          <small>{item.domainLabel}</small>
                          {item.description ? <small>{item.description}</small> : null}
                          {ambiguousSearchRecords.has(semanticRecordRefKey(item.record)) ? (
                            <small>{semanticRecordDiscriminator(item.record)}</small>
                          ) : null}
                        </span>
                        {item.changeKind ? (
                          <DifferenceBadge kind={item.changeKind} />
                        ) : null}
                      </button>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="km-workbench-empty">{t('semanticExplore.search.empty')}</p>
              )}
              <LoadMoreButton
                count={controller.search.data.items.length}
                isBusy={controller.search.isAppending}
                nextCursor={controller.search.data.nextCursor}
                onLoad={() => void controller.loadMoreSearch()}
              />
            </>
          ) : null}
        </QueryBoundary>
      </div>

      <EntityPage
        controller={controller}
        onNavigateEntity={onNavigateEntity}
      />
    </div>
  );
}

function EntityPage({
  controller,
  onNavigateEntity
}: Pick<SemanticExploreSectionProps, 'controller' | 'onNavigateEntity'>) {
  const { t, translateLiteral } = useLocalization();
  const entity = controller.entity.data?.entity;
  const fieldGroups = entity
    ? groupEntityFields(entity.fields, t('analysisPresentation.group.details'))
    : [];
  return (
    <aside className="km-semantic-entity" aria-label={t('semanticExplore.entity.title')}>
      <QueryBoundary state={controller.entity} idleKey="semanticExplore.entity.empty">
        {entity ? (
          <>
            <header>
              <div data-localization-ignore="true">
                <small>{entity.record.domain}</small>
                <h3>{entity.title}</h3>
                {entity.summary ? <p>{entity.summary}</p> : null}
              </div>
              <button
                className="secondary-button compact-button"
                onClick={() => onNavigateEntity(entity.record)}
                type="button"
              >
                {t('semanticExplore.entity.openEditor')}
                <ArrowRight aria-hidden="true" size={15} />
              </button>
            </header>
            <CoverageSummary coverage={controller.entity.data?.coverage ?? []} />
            <div className="km-analysis-field-groups">
              {fieldGroups.map((group) => (
                <section className="km-analysis-field-group" key={group.key}>
                  <h4 data-localization-ignore="true">{humanizeIdentifier(group.key)}</h4>
                  <dl className="km-semantic-field-list">
                    {group.fields.map((field) => {
                      const value = presentFactValue(
                        field.label,
                        field.value.displayValue,
                        null,
                        translateLiteral
                      );
                      return (
                        <div key={field.key}>
                          <dt data-localization-ignore="true"><span>{field.label}</span></dt>
                          <dd data-localization-ignore="true">
                            {value.displayValue}
                            <TechnicalDetails summary={translateLiteral('Technical details')}>
                              <code>{field.key}</code>
                              {value.changed ? <code>{value.exactValue}</code> : null}
                            </TechnicalDetails>
                          </dd>
                        </div>
                      );
                    })}
                  </dl>
                </section>
              ))}
            </div>
          </>
        ) : null}
      </QueryBoundary>
    </aside>
  );
}

function CompareModulePanel({
  controller,
  externalComparisonDisabled,
  onNavigateEntity,
  onPickExternalMod
}: Pick<
  SemanticExploreSectionProps,
  | 'controller'
  | 'externalComparisonDisabled'
  | 'onNavigateEntity'
  | 'onPickExternalMod'
>) {
  const { t } = useLocalization();
  const layers = availableQueryableLayers(controller);
  const recordOptions = availableRecordOptions(controller);
  const [left, setLeft] = useState<QueryableLayer>('base');
  const [right, setRight] = useState<QueryableLayer>(() => preferredLayer(controller));
  const [recordKey, setRecordKey] = useState('');
  const [externalPickFailed, setExternalPickFailed] = useState(false);
  const initializedRevisionRef = useRef<string | null>(null);
  const pickerGenerationRef = useRef(0);
  const revisionIdentityRef = useRef(
    semanticRevisionIdentity(controller.capabilities.data?.revision)
  );
  revisionIdentityRef.current = semanticRevisionIdentity(
    controller.capabilities.data?.revision
  );
  useEffect(() => () => {
    pickerGenerationRef.current += 1;
  }, []);
  useEffect(() => {
    const revision = controller.capabilities.data?.revision.fingerprint ?? null;
    if (revision && initializedRevisionRef.current !== revision) {
      initializedRevisionRef.current = revision;
      setLeft(layers.includes('base') ? 'base' : layers[0] ?? 'base');
      setRight(preferredLayer(controller));
      return;
    }
    const nextLeft = layers.includes(left) ? left : layers[0] ?? 'base';
    let nextRight = layers.includes(right) ? right : preferredLayer(controller);
    if (nextRight === nextLeft) {
      nextRight = layers.find((layer) => layer !== nextLeft) ?? nextLeft;
    }
    if (nextLeft !== left) setLeft(nextLeft);
    if (nextRight !== right) setRight(nextRight);
  }, [controller.capabilities.data?.revision.fingerprint, layers, left, right]);
  const canExternal = controller.capabilities.data?.providers.some(
    (provider) => provider.features.includes('externalCompare')
  ) ?? false;
  const comparison = controller.externalComparison.data ?? controller.comparison.data;
  const submittedComparisonSpec = controller.submittedComparisonSpec;
  const state = controller.externalComparison.status !== 'idle'
    ? controller.externalComparison
    : controller.comparison;
  const selectedRecord = recordOptions.find((option) => option.key === recordKey)?.record ?? null;
  const comparisonMatchesInputs = Boolean(
    comparison &&
    submittedComparisonSpec &&
    selectedRecord &&
    submittedComparisonSpec.record &&
    semanticRecordRefKey(submittedComparisonSpec.record) === semanticRecordRefKey(selectedRecord) &&
    submittedComparisonSpec.left === left &&
    (submittedComparisonSpec.kind === 'external' || submittedComparisonSpec.right === right)
  );
  const visibleComparison = comparisonMatchesInputs ? comparison : null;

  useEffect(() => {
    if (recordKey && !recordOptions.some((option) => option.key === recordKey)) {
      setRecordKey('');
    }
  }, [recordKey, recordOptions]);

  const runExternalComparison = async () => {
    const pickerGeneration = ++pickerGenerationRef.current;
    const expectedRevisionIdentity = revisionIdentityRef.current;
    const expectedLeft = left;
    setExternalPickFailed(false);
    try {
      const externalRootPath = await onPickExternalMod();
      if (
        !externalRootPath ||
        pickerGenerationRef.current !== pickerGeneration ||
        expectedRevisionIdentity === null ||
        revisionIdentityRef.current !== expectedRevisionIdentity
      ) return;
      void controller.compareExternal({
        externalRootPath,
        left: expectedLeft,
        ...(selectedRecord ? { record: selectedRecord } : {})
      });
    } catch {
      if (
        pickerGenerationRef.current === pickerGeneration &&
        revisionIdentityRef.current === expectedRevisionIdentity
      ) {
        setExternalPickFailed(true);
      }
    }
  };

  return (
    <div>
      <div className="km-semantic-query-bar">
        <LayerSelect labelKey="semanticExplore.compare.left" layers={layers} onChange={setLeft} value={left} />
        <LayerSelect labelKey="semanticExplore.compare.right" layers={layers} onChange={setRight} value={right} />
        <RecordSearchPicker
          controller={controller}
          layer={right}
          onRecordKeyChange={setRecordKey}
          recordKey={recordKey}
          recordOptions={recordOptions}
        />
        <button
          disabled={controller.isQuerying || left === right || !selectedRecord}
          onClick={() => void controller.compare({
            left,
            right,
            record: selectedRecord!
          })}
          type="button"
        >
          {t('semanticExplore.compare.action')}
        </button>
        {canExternal ? (
          <button
            className="secondary-button"
            disabled={externalComparisonDisabled || controller.isQuerying || !selectedRecord}
            onClick={() => void runExternalComparison()}
            type="button"
          >
            <FolderSearch aria-hidden="true" size={16} />
            {t('semanticExplore.external.action')}
          </button>
        ) : null}
      </div>
      {externalComparisonDisabled && canExternal ? (
        <p className="km-semantic-advisory">{t('semanticExplore.external.busy')}</p>
      ) : null}
      {externalPickFailed ? (
        <>
          <PublishCommonEditorError
            domain="analysis.semanticExplore"
            field="externalComparison"
            message={t('semanticExplore.query.error.generic')}
          />
          <p className="km-semantic-query-error" role="alert">
            {t('semanticExplore.query.error.generic')}
          </p>
        </>
      ) : null}
      <QueryBoundary state={state} idleKey="semanticExplore.compare.empty">
        {!visibleComparison && state.status === 'ready' ? (
          <p className="km-workbench-empty">{t('semanticExplore.compare.empty')}</p>
        ) : null}
        {visibleComparison && submittedComparisonSpec ? (
          <p className="km-semantic-query-summary">
            <strong>{t('semanticExplore.compare.left')}:</strong>{' '}
            {t(`semanticExplore.layer.${submittedComparisonSpec.left}`)}{' '}
            <strong>{t('semanticExplore.compare.right')}:</strong>{' '}
            {t(submittedComparisonSpec.kind === 'external'
              ? 'semanticExplore.external.action'
              : `semanticExplore.layer.${submittedComparisonSpec.right}`)}
            {submittedComparisonSpec.record ? (
              <code data-localization-ignore="true">
                {semanticRecordDiscriminator(submittedComparisonSpec.record)}
              </code>
            ) : null}
          </p>
        ) : null}
        {visibleComparison ? (
          <DifferenceList
            differences={visibleComparison.items}
            key={visibleComparison.queryFingerprint}
            onNavigateEntity={onNavigateEntity}
          />
        ) : null}
        {visibleComparison ? <CoverageSummary coverage={visibleComparison.coverage} /> : null}
        {visibleComparison ? (
          <LoadMoreButton
            count={visibleComparison.items.length}
            isBusy={state.isAppending}
            nextCursor={visibleComparison.nextCursor}
            onLoad={() => void (
              controller.externalComparison.data
                ? controller.loadMoreExternalComparison()
                : controller.loadMoreComparison()
            )}
          />
        ) : null}
      </QueryBoundary>
    </div>
  );
}

function RecordSearchPicker({
  controller,
  layer,
  onRecordKeyChange,
  recordKey,
  recordOptions
}: {
  controller: SemanticExploreController;
  layer: QueryableLayer;
  onRecordKeyChange: (recordKey: string) => void;
  recordKey: string;
  recordOptions: readonly {
    key: string;
    label: string;
    record: SemanticExploreRecordRef;
  }[];
}) {
  const { t, translateLiteral } = useLocalization();
  const [query, setQuery] = useState('');
  const [submittedQuery, setSubmittedQuery] = useState('');
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const selected = recordOptions.find((option) => option.key === recordKey) ?? null;
  const allMatches = normalizedQuery
    ? recordOptions.filter((option) => [
      option.label,
      semanticRecordDiscriminator(option.record)
    ].join('\n').toLocaleLowerCase().includes(normalizedQuery) && option.key !== recordKey)
    : [];
  const matches = allMatches.slice(0, 8);
  const search = () => {
    if (!query.trim()) return;
    setSubmittedQuery(normalizedQuery);
    void controller.searchEntities({ layer, searchText: query });
  };
  const searchData = submittedQuery === normalizedQuery ? controller.search.data : null;

  useEffect(() => {
    setSubmittedQuery('');
  }, [layer]);

  return (
    <div className="km-semantic-record-picker">
      <div className="km-semantic-record-picker-search">
        <label>
          <span>{t('semanticExplore.search.label')}</span>
          <input
            maxLength={256}
            onChange={(event) => setQuery(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key !== 'Enter' || !query.trim()) return;
              event.preventDefault();
              search();
            }}
            placeholder={t('semanticExplore.search.placeholder')}
            type="search"
            value={query}
          />
        </label>
        <button
          className="secondary-button"
          disabled={controller.isQuerying || !query.trim()}
          onClick={search}
          type="button"
        >
          <Search aria-hidden="true" size={16} />
          {t('semanticExplore.search.action')}
        </button>
      </div>
      {selected ? (
        <div className="km-semantic-selected-record" role="status">
          <span data-localization-ignore="true">
            <strong>{selected.label}</strong>
            <small>{semanticRecordDiscriminator(selected.record)}</small>
          </span>
          <button
            className="secondary-button compact-button"
            onClick={() => onRecordKeyChange('')}
            type="button"
          >
            {t('analysisPresentation.controls.clearSelection')}
          </button>
        </div>
      ) : null}
      {normalizedQuery ? (
        <>
          <p aria-live="polite" className="km-semantic-record-match-count">
            {t('semanticExplore.search.matches', {
              count: allMatches.length,
              shown: matches.length
            })}
          </p>
          {matches.length > 0 ? (
            <ul aria-label={t('analysisPresentation.controls.record')} className="km-semantic-record-matches">
              {matches.map((option) => {
                const exactName = `${option.label}, ${semanticRecordDiscriminator(option.record)}`;
                return (
                  <li key={option.key}>
                    <span data-localization-ignore="true">
                      <strong>{option.label}</strong>
                      <small>{semanticRecordDiscriminator(option.record)}</small>
                    </span>
                    <button
                      aria-label={`${translateLiteral('Add')}: ${exactName}`}
                      className="secondary-button compact-button"
                      onClick={() => {
                        onRecordKeyChange(option.key);
                        setQuery('');
                      }}
                      type="button"
                    >
                      {translateLiteral('Add')}
                    </button>
                  </li>
                );
              })}
            </ul>
          ) : (
            <p className="km-workbench-empty">{t('analysisPresentation.controls.noMatches')}</p>
          )}
          {searchData ? (
            <LoadMoreButton
              count={searchData.items.length}
              isBusy={controller.search.isAppending}
              nextCursor={searchData.nextCursor}
              onLoad={() => void controller.loadMoreSearch()}
            />
          ) : null}
        </>
      ) : null}
    </div>
  );
}

function DifferenceList({
  differences,
  onNavigateEntity
}: {
  differences: readonly SemanticExploreDifference[];
  onNavigateEntity: (record: SemanticExploreRecordRef) => void;
}) {
  const { t } = useLocalization();
  const [resultFilter, setResultFilter] = useState('');
  const [fieldFilter, setFieldFilter] = useState('all');
  const [recordFilter, setRecordFilter] = useState('all');
  const [resultOrder, setResultOrder] = useState<'record' | 'field' | 'kind'>('record');
  const differenceEntries = useMemo(() => {
    const occurrences = new Map<string, number>();
    return differences.map((difference) => {
      const identity = semanticDifferenceIdentity(difference);
      const occurrence = occurrences.get(identity) ?? 0;
      occurrences.set(identity, occurrence + 1);
      return {
        difference,
        key: JSON.stringify([identity, occurrence])
      };
    });
  }, [differences]);
  const fields = useMemo(() => [...new Map(differences.map((difference) => [
    difference.fieldKey,
    difference.label
  ])).entries()].sort((left, right) => left[1].localeCompare(right[1])), [differences]);
  const records = useMemo(() => [...new Map(differences.map((difference) => {
    const key = semanticRecordRefKey(difference.record);
    return [key, semanticRecordDiscriminator(difference.record)] as const;
  })).entries()].sort((left, right) => left[1].localeCompare(right[1])), [differences]);
  useEffect(() => {
    if (fieldFilter !== 'all' && !fields.some(([key]) => key === fieldFilter)) {
      setFieldFilter('all');
    }
    if (recordFilter !== 'all' && !records.some(([key]) => key === recordFilter)) {
      setRecordFilter('all');
    }
  }, [fieldFilter, fields, recordFilter, records]);
  const matchingDifferences = useMemo(() => {
    const normalizedFilter = resultFilter.trim().toLocaleLowerCase();
    return [...differenceEntries]
      .filter(({ difference }) => {
        return (
          (fieldFilter === 'all' || difference.fieldKey === fieldFilter) &&
          (recordFilter === 'all' || semanticRecordRefKey(difference.record) === recordFilter) &&
          (
            !normalizedFilter ||
            difference.label.toLocaleLowerCase().includes(normalizedFilter) ||
            semanticRecordDiscriminator(difference.record)
              .toLocaleLowerCase()
              .includes(normalizedFilter) ||
            difference.left?.displayValue.toLocaleLowerCase().includes(normalizedFilter) ||
            difference.right?.displayValue.toLocaleLowerCase().includes(normalizedFilter)
          )
        );
      })
      .sort(({ difference: left }, { difference: right }) => {
        if (resultOrder === 'field') {
          return left.label.localeCompare(right.label) ||
            semanticRecordDiscriminator(left.record).localeCompare(
              semanticRecordDiscriminator(right.record)
            );
        }
        if (resultOrder === 'kind') {
          return left.kind.localeCompare(right.kind) ||
            semanticRecordDiscriminator(left.record).localeCompare(
              semanticRecordDiscriminator(right.record)
            );
        }
        return semanticRecordDiscriminator(left.record).localeCompare(
          semanticRecordDiscriminator(right.record)
        ) ||
          left.label.localeCompare(right.label);
      });
  }, [differenceEntries, fieldFilter, recordFilter, resultFilter, resultOrder]);
  if (differences.length === 0) {
    return <p className="km-workbench-empty">{t('semanticExplore.compare.noDifferences')}</p>;
  }
  return (
    <>
      <div className="km-semantic-result-controls">
        <label>
          <span>{t('analysisPresentation.controls.filter')}</span>
          <input
            onChange={(event) => setResultFilter(event.currentTarget.value)}
            type="search"
            value={resultFilter}
          />
        </label>
        <label>
          <span>{t('analysisPresentation.controls.record')}</span>
          <select
            className="km-select-control"
            onChange={(event) => setRecordFilter(event.currentTarget.value)}
            value={recordFilter}
          >
            <option value="all">{t('analysisPresentation.controls.allRecords')}</option>
            {records.map(([key, label]) => (
              <option data-localization-ignore="true" key={key} value={key}>{label}</option>
            ))}
          </select>
        </label>
        <label>
          <span>{t('analysisPresentation.controls.field')}</span>
          <select
            className="km-select-control"
            onChange={(event) => setFieldFilter(event.currentTarget.value)}
            value={fieldFilter}
          >
            <option value="all">{t('analysisPresentation.controls.allFields')}</option>
            {fields.map(([key, label]) => (
              <option data-localization-ignore="true" key={key} value={key}>{label}</option>
            ))}
          </select>
        </label>
        <label>
          <span>{t('analysisPresentation.controls.sort')}</span>
          <select
            className="km-select-control"
            onChange={(event) => setResultOrder(event.currentTarget.value as typeof resultOrder)}
            value={resultOrder}
          >
            <option value="record">{t('analysisPresentation.controls.record')}</option>
            <option value="field">{t('analysisPresentation.controls.field')}</option>
            <option value="kind">{t('analysisPresentation.controls.resultType')}</option>
          </select>
        </label>
      </div>
      {matchingDifferences.length === 0 ? (
        <p className="km-workbench-empty">{t('analysisPresentation.controls.noMatches')}</p>
      ) : null}
      <ul className="km-semantic-difference-list">
      {matchingDifferences.map(({ difference, key }) => {
        return (
        <li key={key}>
          <button
            aria-label={`${t('semanticExplore.entity.openEditor')}: ${semanticRecordDiscriminator(difference.record)}`}
            className="km-semantic-record-link"
            onClick={() => onNavigateEntity(difference.record)}
            type="button"
          >
            <ArrowRight aria-hidden="true" size={14} />
          </button>
          <div>
            <span data-localization-ignore="true">
              <strong>{difference.label}</strong>
              <small>{semanticRecordDiscriminator(difference.record)}</small>
            </span>
            <DifferenceBadge kind={difference.kind} />
          </div>
          <div className="km-semantic-value-pair">
            <SemanticValue value={difference.left} />
            <ArrowRight aria-hidden="true" size={14} />
            <SemanticValue value={difference.right} />
          </div>
        </li>
        );
      })}
      </ul>
    </>
  );
}

function OwnershipModulePanel({
  controller,
  onNavigateEntity
}: Pick<SemanticExploreSectionProps, 'controller' | 'onNavigateEntity'>) {
  const { t } = useLocalization();
  const ownership = controller.ownership.data;
  const submittedOwnershipSpec = controller.submittedOwnershipSpec;
  const recordOptions = availableRecordOptions(controller);
  const [recordKey, setRecordKey] = useState('');
  const selectedRecord = recordOptions.find((option) => option.key === recordKey)?.record ?? null;
  const ownershipMatchesInput = Boolean(
    ownership &&
    submittedOwnershipSpec?.record &&
    selectedRecord &&
    semanticRecordRefKey(submittedOwnershipSpec.record) === semanticRecordRefKey(selectedRecord)
  );
  const visibleOwnership = ownershipMatchesInput ? ownership : null;
  const nodeLabels = new Map(
    visibleOwnership?.nodes.map((node) => [node.nodeId, node.label]) ?? []
  );
  useEffect(() => {
    if (recordKey && !recordOptions.some((option) => option.key === recordKey)) {
      setRecordKey('');
    }
  }, [recordKey, recordOptions]);
  return (
    <div>
      <div className="km-semantic-query-bar">
        <RecordSearchPicker
          controller={controller}
          layer={preferredLayer(controller)}
          onRecordKeyChange={setRecordKey}
          recordKey={recordKey}
          recordOptions={recordOptions}
        />
        <button
          disabled={controller.ownership.status === 'loading' || !selectedRecord}
          onClick={() => void controller.getOwnership(selectedRecord!)}
          type="button"
        >
          <Network aria-hidden="true" size={16} />
          {t('semanticExplore.ownership.load')}
        </button>
      </div>
      <QueryBoundary state={controller.ownership} idleKey="semanticExplore.ownership.empty">
        {!visibleOwnership && controller.ownership.status === 'ready' ? (
          <p className="km-workbench-empty">{t('semanticExplore.ownership.empty')}</p>
        ) : null}
        {visibleOwnership ? (
          <>
            {submittedOwnershipSpec ? (
              <p className="km-semantic-query-summary">
                <strong>{t('analysisPresentation.controls.record')}:</strong>{' '}
                {submittedOwnershipSpec.record ? (
                  <code data-localization-ignore="true">
                    {semanticRecordDiscriminator(submittedOwnershipSpec.record)}
                  </code>
                ) : t('analysisPresentation.controls.allRecords')}
              </p>
            ) : null}
            <CoverageSummary coverage={visibleOwnership.coverage} />
            {visibleOwnership.conflicts.length > 0 ? (
              <section className="km-semantic-conflicts">
                <h3>{t('semanticExplore.ownership.conflicts')}</h3>
                <ul>
                  {visibleOwnership.conflicts.map((conflict) => (
                    <li key={conflict.conflictId} data-severity={conflict.severity}>
                      <span data-localization-ignore="true">{conflict.label}</span>
                      <small>{t(`semanticExplore.severity.${conflict.severity}`)}</small>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
            <div className="km-semantic-graph" role="list">
              {visibleOwnership.nodes.map((node) => (
                <article key={node.nodeId} role="listitem">
                  <Boxes aria-hidden="true" size={16} />
                  <span data-localization-ignore="true">
                    <strong>{node.label}</strong>
                  </span>
                  <small>{t(`semanticExplore.ownership.node.${node.kind}`)}</small>
                  {node.record ? (
                    <button
                      aria-label={`${t('semanticExplore.entity.openEditor')}: ${node.label}, ${semanticRecordDiscriminator(node.record)}`}
                      className="secondary-button compact-button"
                      onClick={() => onNavigateEntity(node.record!)}
                      type="button"
                    >
                      {t('semanticExplore.entity.openEditor')}
                    </button>
                  ) : null}
                </article>
              ))}
              {visibleOwnership.edges.map((edge, index) => (
                <p
                  className="km-semantic-edge"
                  key={`${edge.sourceNodeId}:${edge.targetNodeId}:${index}`}
                  role="listitem"
                >
                  <span data-localization-ignore="true">
                    {nodeLabels.get(edge.sourceNodeId) ?? humanizeIdentifier(edge.sourceNodeId)}
                  </span>
                  <strong>{t(`semanticExplore.ownership.edge.${edge.kind}`)}</strong>
                  <span data-localization-ignore="true">
                    {nodeLabels.get(edge.targetNodeId) ?? humanizeIdentifier(edge.targetNodeId)}
                  </span>
                </p>
              ))}
            </div>
            <LoadMoreButton
              count={Math.max(
                visibleOwnership.nodes.length,
                visibleOwnership.edges.length,
                visibleOwnership.conflicts.length
              )}
              isBusy={controller.ownership.isAppending}
              nextCursor={visibleOwnership.nextCursor}
              onLoad={() => void controller.loadMoreOwnership()}
            />
          </>
        ) : null}
      </QueryBoundary>
    </div>
  );
}

function ChangesModulePanel({
  controller,
  onNavigateEntity
}: Pick<SemanticExploreSectionProps, 'controller' | 'onNavigateEntity'>) {
  const { t } = useLocalization();
  const [from, setFrom] = useState<'base' | 'layered'>('base');
  const [to, setTo] = useState<'layered' | 'pending'>('pending');
  const [format, setFormat] = useState<'structured' | 'canonicalText'>('structured');
  const [resultFilter, setResultFilter] = useState('');
  const [fieldFilter, setFieldFilter] = useState('all');
  const [resultOrder, setResultOrder] = useState<'path' | 'field' | 'kind'>('path');
  const layers = availableQueryableLayers(controller);
  const hasLayered = layers.includes('layered');
  const hasPending = layers.includes('pending');
  useEffect(() => {
    if (from === 'layered' && !hasLayered) setFrom('base');
    if (to === 'pending' && !hasPending) setTo(hasLayered ? 'layered' : 'pending');
  }, [from, hasLayered, hasPending, to]);
  const changes = controller.changes.data;
  const submittedChangesSpec = controller.submittedChangesSpec;
  const fieldKeys = useMemo(
    () => [...new Set(changes?.items.map((change) => change.fieldKey) ?? [])].sort(),
    [changes?.items]
  );
  useEffect(() => {
    if (fieldFilter !== 'all' && !fieldKeys.includes(fieldFilter)) setFieldFilter('all');
  }, [fieldFilter, fieldKeys]);
  const visibleChanges = useMemo(() => {
    const normalizedFilter = resultFilter.trim().toLocaleLowerCase();
    return [...(changes?.items ?? [])]
      .filter((change) => (
        (fieldFilter === 'all' || change.fieldKey === fieldFilter) &&
        (
          !normalizedFilter ||
          change.path.toLocaleLowerCase().includes(normalizedFilter) ||
          change.fieldKey.toLocaleLowerCase().includes(normalizedFilter) ||
          change.line.toLocaleLowerCase().includes(normalizedFilter)
        )
      ))
      .sort((left, right) => {
        if (resultOrder === 'field') {
          return left.fieldKey.localeCompare(right.fieldKey) || left.path.localeCompare(right.path);
        }
        if (resultOrder === 'kind') {
          return left.kind.localeCompare(right.kind) || left.path.localeCompare(right.path);
        }
        return left.path.localeCompare(right.path) || left.fieldKey.localeCompare(right.fieldKey);
      });
  }, [changes?.items, fieldFilter, resultFilter, resultOrder]);
  return (
    <div>
      <div className="km-semantic-query-bar">
        <label>
          <span>{t('semanticExplore.changes.from')}</span>
          <select
            className="km-select-control"
            onChange={(event) => setFrom(event.currentTarget.value as typeof from)}
            value={from}
          >
            <option value="base">{t('semanticExplore.layer.base')}</option>
            {hasLayered ? <option value="layered">{t('semanticExplore.layer.layered')}</option> : null}
          </select>
        </label>
        <label>
          <span>{t('semanticExplore.changes.to')}</span>
          <select
            className="km-select-control"
            onChange={(event) => setTo(event.currentTarget.value as typeof to)}
            value={to}
          >
            {hasLayered ? <option value="layered">{t('semanticExplore.layer.layered')}</option> : null}
            {hasPending ? <option value="pending">{t('semanticExplore.layer.pending')}</option> : null}
          </select>
        </label>
        <label>
          <span>{t('semanticExplore.changes.format')}</span>
          <select
            className="km-select-control"
            onChange={(event) => setFormat(event.currentTarget.value as typeof format)}
            value={format}
          >
            <option value="structured">{t('semanticExplore.changes.structured')}</option>
            <option value="canonicalText">{t('semanticExplore.changes.canonicalText')}</option>
          </select>
        </label>
        <button
          disabled={
            controller.changes.status === 'loading' ||
            !layers.includes(to) ||
            (from === 'layered' && to === 'layered')
          }
          onClick={() => void controller.getSemanticChanges({ format, from, to })}
          type="button"
        >
          <FileDiff aria-hidden="true" size={16} />
          {t('semanticExplore.changes.load')}
        </button>
      </div>
      <QueryBoundary state={controller.changes} idleKey="semanticExplore.changes.empty">
        {changes && submittedChangesSpec ? (
          <p className="km-semantic-query-summary">
            <strong>{t('semanticExplore.changes.from')}:</strong>{' '}
            {t(`semanticExplore.layer.${submittedChangesSpec.from}`)}{' '}
            <strong>{t('semanticExplore.changes.to')}:</strong>{' '}
            {t(`semanticExplore.layer.${submittedChangesSpec.to}`)}{' '}
            <strong>{t('semanticExplore.changes.format')}:</strong>{' '}
            {t(`semanticExplore.changes.${submittedChangesSpec.format}`)}
          </p>
        ) : null}
        {changes ? <CoverageSummary coverage={changes.coverage} /> : null}
        {changes && changes.items.length > 0 ? (
          <div className="km-semantic-result-controls">
            <label>
              <span>{t('analysisPresentation.controls.filter')}</span>
              <input
                onChange={(event) => setResultFilter(event.currentTarget.value)}
                type="search"
                value={resultFilter}
              />
            </label>
            <label>
              <span>{t('analysisPresentation.controls.field')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setFieldFilter(event.currentTarget.value)}
                value={fieldFilter}
              >
                <option value="all">{t('analysisPresentation.controls.allFields')}</option>
                {fieldKeys.map((fieldKey) => (
                  <option data-localization-ignore="true" key={fieldKey} value={fieldKey}>
                    {humanizeIdentifier(fieldKey)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              <span>{t('analysisPresentation.controls.sort')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setResultOrder(event.currentTarget.value as typeof resultOrder)}
                value={resultOrder}
              >
                <option value="path">{t('analysisPresentation.controls.path')}</option>
                <option value="field">{t('analysisPresentation.controls.field')}</option>
                <option value="kind">{t('analysisPresentation.controls.resultType')}</option>
              </select>
            </label>
          </div>
        ) : null}
        {changes && submittedChangesSpec?.format === 'canonicalText' ? (
          <pre
            aria-label={`${t('semanticExplore.changes.canonicalText')}: ${submittedChangesSpec.from} ${submittedChangesSpec.to}`}
            className="km-semantic-change-text"
            data-localization-ignore="true"
            role="region"
            tabIndex={0}
          >
            {visibleChanges.map((change) => change.line).join('\n')}
          </pre>
        ) : null}
        {changes && submittedChangesSpec?.format === 'structured' ? (
          <ul className="km-semantic-change-list">
            {visibleChanges.map((change, index) => (
              <li key={`${change.path}:${change.fieldKey}:${index}`}>
                <button
                  aria-label={`${t('semanticExplore.entity.openEditor')}: ${change.path}, ${change.fieldKey}, ${semanticRecordDiscriminator(change.record)}`}
                  className="km-semantic-record-link"
                  onClick={() => onNavigateEntity(change.record)}
                  type="button"
                >
                  <ArrowRight aria-hidden="true" size={14} />
                </button>
                <span data-localization-ignore="true">
                  <strong>{change.path}</strong>
                  <small>{change.fieldKey}</small>
                </span>
                <DifferenceBadge kind={change.kind} />
                <div className="km-semantic-value-pair">
                  <SemanticValue value={change.before} />
                  <ArrowRight aria-hidden="true" size={14} />
                  <SemanticValue value={change.after} />
                </div>
              </li>
            ))}
          </ul>
        ) : null}
        {changes && changes.items.length > 0 && visibleChanges.length === 0 ? (
          <p className="km-workbench-empty">{t('analysisPresentation.controls.noMatches')}</p>
        ) : null}
        {changes ? (
          <LoadMoreButton
            count={changes.items.length}
            isBusy={controller.changes.isAppending}
            nextCursor={changes.nextCursor}
            onLoad={() => void controller.loadMoreChanges()}
          />
        ) : null}
      </QueryBoundary>
    </div>
  );
}

function QueryBoundary<T>({
  children,
  idleKey = 'semanticExplore.query.idle',
  state
}: {
  children: ReactNode;
  idleKey?: string;
  state: SemanticQueryState<T>;
}) {
  const { t } = useLocalization();
  const errorMessage = state.status === 'error'
    ? t(`semanticExplore.query.error.${state.error ?? 'generic'}`)
    : null;
  usePublishCommonEditorError({ domain: 'analysis.semanticExplore', message: errorMessage });
  if (state.status === 'idle') {
    return <p className="km-workbench-empty">{t(idleKey)}</p>;
  }
  if (state.status === 'loading' && !state.data) {
    return <SemanticStatus kind="loading" />;
  }
  return (
    <>
      {state.status === 'loading' && state.data && !state.isAppending ? (
        <LoadingProgress className="is-compact" label={t('semanticExplore.loading')} />
      ) : null}
      {state.status === 'error' ? (
        <p className="km-semantic-query-error" role="alert">
          {errorMessage}
        </p>
      ) : null}
      {children}
    </>
  );
}

function SemanticStatus({
  error,
  kind,
  onRetry
}: {
  error?: SemanticQueryError | null;
  kind: 'loading' | 'error';
  onRetry?: () => void;
}) {
  const { t } = useLocalization();
  const label = t(kind === 'error' && error
    ? `semanticExplore.query.error.${error}`
    : `semanticExplore.${kind}`);
  usePublishCommonEditorError({
    domain: 'analysis.semanticExplore',
    message: kind === 'error' ? label : null
  });
  if (kind === 'loading') {
    return (
      <div className="km-semantic-status">
        <LoadingProgress label={label} />
      </div>
    );
  }
  return (
    <div className="km-semantic-status" role="alert">
      <p>{label}</p>
      {onRetry ? (
        <button className="secondary-button compact-button" onClick={onRetry} type="button">
          {t('semanticExplore.retry')}
        </button>
      ) : null}
    </div>
  );
}

function CoverageSummary({ coverage }: { coverage: readonly SemanticExploreCoverage[] }) {
  const { t, translateLiteral } = useLocalization();
  if (coverage.length === 0) return null;
  return (
    <details className="km-semantic-coverage">
      <summary>{t('semanticExplore.coverage.title')}</summary>
      <ul>
        {coverage.map((entry) => (
          <li key={entry.providerId}>
            <span data-localization-ignore="true">{humanizeIdentifier(entry.providerId)}</span>
            <span data-localization-ignore="true">
              {entry.domains.map(humanizeIdentifier).join(', ')}
            </span>
            <span>{t(`semanticExplore.coverage.state.${entry.state}`)}</span>
            <span>{t(`semanticExplore.coverage.confidence.${entry.confidence}`)}</span>
            {entry.reasonCode ? (
              <span>{t('analysisPresentation.coverage.limited')}</span>
            ) : null}
            <TechnicalDetails summary={translateLiteral('Technical details')}>
              <code>{entry.providerId}</code>
              {entry.domains.map((domain) => <code key={domain}>{domain}</code>)}
              {entry.reasonCode ? <code>{entry.reasonCode}</code> : null}
            </TechnicalDetails>
          </li>
        ))}
      </ul>
      <p>{t('semanticExplore.coverage.disclaimer')}</p>
    </details>
  );
}

function groupEntityFields<T extends { group: string }>(fields: readonly T[], fallback: string) {
  const groups = new Map<string, T[]>();
  for (const field of fields) {
    const key = field.group.trim() || fallback;
    const values = groups.get(key);
    if (values) values.push(field);
    else groups.set(key, [field]);
  }
  return [...groups].map(([key, groupedFields]) => ({ fields: groupedFields, key }));
}

function LayerSelect({
  labelKey = 'semanticExplore.layer.label',
  layers,
  onChange,
  value
}: {
  labelKey?: string;
  layers: readonly QueryableLayer[];
  onChange: (layer: QueryableLayer) => void;
  value: QueryableLayer;
}) {
  const { t } = useLocalization();
  return (
    <label>
      <span>{t(labelKey)}</span>
      <select
        className="km-select-control"
        onChange={(event) => onChange(event.currentTarget.value as QueryableLayer)}
        value={value}
      >
        {layers.map((layer) => (
          <option key={layer} value={layer}>{t(`semanticExplore.layer.${layer}`)}</option>
        ))}
      </select>
    </label>
  );
}

function DifferenceBadge({ kind }: { kind: string }) {
  const { t } = useLocalization();
  return (
    <span className="km-semantic-difference-badge" data-kind={kind}>
      {t(`semanticExplore.difference.${kind}`)}
    </span>
  );
}

function SemanticValue({ value }: { value: SemanticExploreScalar | null }) {
  const { t } = useLocalization();
  return (
    <code data-localization-ignore={value ? 'true' : undefined}>
      {value?.displayValue ?? t('semanticExplore.value.unavailable')}
    </code>
  );
}

function LoadMoreButton({
  count,
  isBusy,
  nextCursor,
  onLoad
}: {
  count: number;
  isBusy: boolean;
  nextCursor: string | null;
  onLoad: () => void;
}) {
  const { t } = useLocalization();
  if (!nextCursor) return null;
  if (count >= semanticExploreMaximumAccumulatedResults) {
    return <p className="km-semantic-advisory">{t('semanticExplore.results.windowLimit')}</p>;
  }
  return (
    <>
      <button
        aria-busy={isBusy || undefined}
        className="secondary-button km-semantic-load-more"
        disabled={isBusy}
        onClick={onLoad}
        type="button"
      >
        {isBusy ? t('semanticExplore.loading') : t('semanticExplore.results.more')}
      </button>
      {isBusy ? (
        <LoadingProgress className="is-compact" label={t('semanticExplore.loading')} />
      ) : null}
    </>
  );
}

function availableQueryableLayers(controller: SemanticExploreController): QueryableLayer[] {
  const layers = controller.capabilities.data?.snapshots
    .map((snapshot) => snapshot.layer.kind)
    .filter((layer): layer is QueryableLayer => layer !== 'comparedMod') ?? ['base'];
  return [...new Set(layers)];
}

function preferredLayer(controller: SemanticExploreController): QueryableLayer {
  const layers = availableQueryableLayers(controller);
  return layers.includes('pending')
    ? 'pending'
    : layers.includes('layered')
      ? 'layered'
      : 'base';
}

function availableRecordOptions(controller: SemanticExploreController) {
  const options = new Map<string, {
    key: string;
    label: string;
    record: SemanticExploreRecordRef;
  }>();
  const entity = controller.entity.data?.entity;
  if (entity) {
    const key = semanticRecordRefKey(entity.record);
    options.set(key, { key, label: entity.title, record: entity.record });
  }
  for (const item of controller.search.data?.items ?? []) {
    const key = semanticRecordRefKey(item.record);
    if (!options.has(key)) {
      options.set(key, { key, label: item.displayName, record: item.record });
    }
  }
  const values = [...options.values()];
  const labelCounts = new Map<string, number>();
  for (const option of values) {
    labelCounts.set(option.label, (labelCounts.get(option.label) ?? 0) + 1);
  }
  return values
    .map((option) => ({
      ...option,
      label: (labelCounts.get(option.label) ?? 0) > 1
        ? `${option.label} - ${semanticRecordDiscriminator(option.record)}`
        : option.label
    }))
    .sort((left, right) => left.label.localeCompare(right.label));
}

function semanticRecordDiscriminator(record: SemanticExploreRecordRef) {
  return [
    humanizeIdentifier(record.gameFamily),
    humanizeIdentifier(record.domain),
    `${humanizeIdentifier(record.recordKind.key)}@${record.recordKind.schemaVersion}`,
    record.recordId,
    record.subrecordId
  ].filter((value): value is string => Boolean(value)).join(' / ');
}

function semanticDifferenceIdentity(difference: SemanticExploreDifference) {
  return JSON.stringify([
    semanticRecordRefKey(difference.record),
    difference.fieldKey,
    difference.ownerId,
    difference.kind,
    difference.label,
    difference.left,
    difference.right
  ]);
}

function semanticRevisionIdentity(revision: SemanticExploreRevision | null | undefined) {
  return revision
    ? JSON.stringify([
      revision.projectId,
      revision.gameFamily,
      revision.generation,
      revision.fingerprint
    ])
    : null;
}

function moduleTabIndex(key: string, currentIndex: number, count: number) {
  switch (key) {
    case 'ArrowLeft':
      return (currentIndex - 1 + count) % count;
    case 'ArrowRight':
      return (currentIndex + 1) % count;
    case 'Home':
      return 0;
    case 'End':
      return count - 1;
    default:
      return null;
  }
}
