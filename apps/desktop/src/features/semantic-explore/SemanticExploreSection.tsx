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
            aria-controls="semantic-explore-module-panel"
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

      <div
        aria-labelledby={`semantic-explore-module-${activeModule}`}
        className="km-semantic-module"
        id="semantic-explore-module-panel"
        role="tabpanel"
      >
        {activeModule === 'explore' ? (
          <ExploreModulePanel controller={controller} onNavigateEntity={onNavigateEntity} />
        ) : null}
        {activeModule === 'compare' ? (
          <CompareModulePanel
            controller={controller}
            externalComparisonDisabled={externalComparisonDisabled}
            onNavigateEntity={onNavigateEntity}
            onPickExternalMod={onPickExternalMod}
          />
        ) : null}
        {activeModule === 'ownership' ? (
          <OwnershipModulePanel controller={controller} onNavigateEntity={onNavigateEntity} />
        ) : null}
        {activeModule === 'changes' ? (
          <ChangesModulePanel controller={controller} onNavigateEntity={onNavigateEntity} />
        ) : null}
      </div>
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
  const [left, setLeft] = useState<QueryableLayer>('base');
  const [right, setRight] = useState<QueryableLayer>(() => preferredLayer(controller));
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
  const state = controller.externalComparison.status !== 'idle'
    ? controller.externalComparison
    : controller.comparison;

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
      void controller.compareExternal({ externalRootPath, left: expectedLeft });
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
        <button
          disabled={controller.isQuerying || left === right}
          onClick={() => void controller.compare({ left, right })}
          type="button"
        >
          {t('semanticExplore.compare.action')}
        </button>
        {canExternal ? (
          <button
            className="secondary-button"
            disabled={externalComparisonDisabled || controller.isQuerying}
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
        <p className="km-semantic-query-error" role="alert">
          {t('semanticExplore.query.error.generic')}
        </p>
      ) : null}
      <QueryBoundary state={state} idleKey="semanticExplore.compare.empty">
        {comparison ? (
          <DifferenceList
            differences={comparison.items}
            onNavigateEntity={onNavigateEntity}
          />
        ) : null}
        {comparison ? <CoverageSummary coverage={comparison.coverage} /> : null}
        {comparison ? (
          <LoadMoreButton
            count={comparison.items.length}
            isBusy={state.isAppending}
            nextCursor={comparison.nextCursor}
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

function DifferenceList({
  differences,
  onNavigateEntity
}: {
  differences: readonly SemanticExploreDifference[];
  onNavigateEntity: (record: SemanticExploreRecordRef) => void;
}) {
  const { t } = useLocalization();
  if (differences.length === 0) {
    return <p className="km-workbench-empty">{t('semanticExplore.compare.noDifferences')}</p>;
  }
  return (
    <ul className="km-semantic-difference-list">
      {differences.map((difference, index) => (
        <li key={`${semanticRecordRefKey(difference.record)}:${difference.fieldKey}:${index}`}>
          <button
            aria-label={t('semanticExplore.entity.openEditor')}
            className="km-semantic-record-link"
            onClick={() => onNavigateEntity(difference.record)}
            type="button"
          >
            <ArrowRight aria-hidden="true" size={14} />
          </button>
          <div>
            <span data-localization-ignore="true">
              <strong>{difference.label}</strong>
              <small>{difference.record.recordId}</small>
            </span>
            <DifferenceBadge kind={difference.kind} />
          </div>
          <div className="km-semantic-value-pair">
            <SemanticValue value={difference.left} />
            <ArrowRight aria-hidden="true" size={14} />
            <SemanticValue value={difference.right} />
          </div>
        </li>
      ))}
    </ul>
  );
}

function OwnershipModulePanel({
  controller,
  onNavigateEntity
}: Pick<SemanticExploreSectionProps, 'controller' | 'onNavigateEntity'>) {
  const { t } = useLocalization();
  const ownership = controller.ownership.data;
  const nodeLabels = new Map(ownership?.nodes.map((node) => [node.nodeId, node.label]) ?? []);
  return (
    <div>
      <div className="km-semantic-query-bar">
        <button
          disabled={controller.ownership.status === 'loading'}
          onClick={() => void controller.getOwnership()}
          type="button"
        >
          <Network aria-hidden="true" size={16} />
          {t('semanticExplore.ownership.load')}
        </button>
      </div>
      <QueryBoundary state={controller.ownership} idleKey="semanticExplore.ownership.empty">
        {ownership ? (
          <>
            <CoverageSummary coverage={ownership.coverage} />
            {ownership.conflicts.length > 0 ? (
              <section className="km-semantic-conflicts">
                <h3>{t('semanticExplore.ownership.conflicts')}</h3>
                <ul>
                  {ownership.conflicts.map((conflict) => (
                    <li key={conflict.conflictId} data-severity={conflict.severity}>
                      <span data-localization-ignore="true">{conflict.label}</span>
                      <small>{t(`semanticExplore.severity.${conflict.severity}`)}</small>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
            <div className="km-semantic-graph" role="list">
              {ownership.nodes.map((node) => (
                <article key={node.nodeId} role="listitem">
                  <Boxes aria-hidden="true" size={16} />
                  <span data-localization-ignore="true">
                    <strong>{node.label}</strong>
                  </span>
                  <small>{t(`semanticExplore.ownership.node.${node.kind}`)}</small>
                  {node.record ? (
                    <button
                      className="secondary-button compact-button"
                      onClick={() => onNavigateEntity(node.record!)}
                      type="button"
                    >
                      {t('semanticExplore.entity.openEditor')}
                    </button>
                  ) : null}
                </article>
              ))}
              {ownership.edges.map((edge, index) => (
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
                ownership.nodes.length,
                ownership.edges.length,
                ownership.conflicts.length
              )}
              isBusy={controller.ownership.isAppending}
              nextCursor={ownership.nextCursor}
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
  const layers = availableQueryableLayers(controller);
  const hasLayered = layers.includes('layered');
  const hasPending = layers.includes('pending');
  useEffect(() => {
    if (from === 'layered' && !hasLayered) setFrom('base');
    if (to === 'pending' && !hasPending) setTo(hasLayered ? 'layered' : 'pending');
  }, [from, hasLayered, hasPending, to]);
  const changes = controller.changes.data;
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
        {changes ? <CoverageSummary coverage={changes.coverage} /> : null}
        {changes && format === 'canonicalText' ? (
          <pre className="km-semantic-change-text" data-localization-ignore="true">
            {changes.items.map((change) => change.line).join('\n')}
          </pre>
        ) : null}
        {changes && format === 'structured' ? (
          <ul className="km-semantic-change-list">
            {changes.items.map((change, index) => (
              <li key={`${change.path}:${change.fieldKey}:${index}`}>
                <button
                  aria-label={t('semanticExplore.entity.openEditor')}
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
          {t(`semanticExplore.query.error.${state.error ?? 'generic'}`)}
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
