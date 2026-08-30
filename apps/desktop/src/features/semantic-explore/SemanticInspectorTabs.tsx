/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useLocalization } from '../../localization';
import type { AdaptiveInspectorTabViewModel } from '../workbench/AdaptiveInspector';
import type {
  SemanticExploreCoverage,
  SemanticExploreRecordRef
} from '../../bridge/semanticExploreContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { semanticRecordRefKey } from '../../workbench/semanticContracts';
import { TechnicalDetails } from '../workbench/AnalysisPresentation';
import { humanizeIdentifier } from '../workbench/analysisPresentationUtils';
import {
  semanticExploreMaximumAccumulatedResults,
  type QueryableLayer,
  type SemanticExploreController,
  type SemanticQueryState
} from './useSemanticExploreController';

export type SemanticInspectorTabsOptions = {
  controller: SemanticExploreController;
  layer: QueryableLayer;
  onNavigateEntity: (record: SemanticExploreRecordRef) => void;
  record: SemanticExploreRecordRef | null;
};

export function useSemanticInspectorTabs({
  controller,
  layer,
  onNavigateEntity,
  record
}: SemanticInspectorTabsOptions): readonly AdaptiveInspectorTabViewModel[] {
  const stableRecord = useMemo<SemanticExploreRecordRef | null>(
    () => record ? {
      domain: record.domain,
      gameFamily: record.gameFamily,
      recordId: record.recordId,
      recordKind: {
        key: record.recordKind.key,
        schemaVersion: record.recordKind.schemaVersion
      },
      subrecordId: record.subrecordId
    } : null,
    [
      record?.domain,
      record?.gameFamily,
      record?.recordId,
      record?.recordKind.key,
      record?.recordKind.schemaVersion,
      record?.subrecordId
    ]
  );
  const recordKey = stableRecord ? semanticRecordRefKey(stableRecord) : null;
  const loadedEntity = controller.entity.data?.entity;
  const hasExactLoadedEntity = Boolean(
    stableRecord &&
    loadedEntity &&
    semanticRecordRefKey(loadedEntity.record) === recordKey &&
    loadedEntity.snapshot.layer.kind === layer
  );
  useEffect(() => {
    if (!stableRecord || hasExactLoadedEntity) return;
    let cancelled = false;
    void controller.ensureCapabilities().then(() => {
      if (!cancelled) {
        void controller.getEntity(stableRecord, layer);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [
    controller.ensureCapabilities,
    controller.getEntity,
    hasExactLoadedEntity,
    layer,
    recordKey,
    stableRecord
  ]);

  return useMemo(() => {
    if (
      !stableRecord ||
      !loadedEntity ||
      semanticRecordRefKey(loadedEntity.record) !== recordKey ||
      loadedEntity.snapshot.layer.kind !== layer
    ) {
      return [];
    }

    const tabs: AdaptiveInspectorTabViewModel[] = [];
    if (loadedEntity.features.compare) {
      tabs.push({
        content: <InspectorCompare controller={controller} layer={layer} record={stableRecord} />,
        count: matchingRecordCount(controller.comparison.data?.items ?? [], stableRecord),
        id: 'compare',
        labelKey: 'semanticExplore.inspector.compare'
      });
    }
    if (loadedEntity.features.references) {
      tabs.push({
        content: (
          <InspectorReferences
            controller={controller}
            layer={layer}
            onNavigateEntity={onNavigateEntity}
            record={stableRecord}
          />
        ),
        count: controller.references.data?.items.length ?? null,
        id: 'references',
        labelKey: 'semanticExplore.inspector.references'
      });
    }
    if (loadedEntity.features.impact) {
      tabs.push({
        content: <InspectorImpact controller={controller} layer={layer} record={stableRecord} />,
        count: controller.impact.data?.items.length ?? null,
        id: 'impact',
        labelKey: 'semanticExplore.inspector.impact'
      });
    }
    if (loadedEntity.features.ownership) {
      tabs.push({
        content: (
          <InspectorOwnership
            controller={controller}
            onNavigateEntity={onNavigateEntity}
            record={stableRecord}
          />
        ),
        count: controller.ownership.data?.conflicts.length ?? null,
        id: 'provenance',
        labelKey: 'semanticExplore.inspector.provenance'
      });
    }
    return tabs;
  }, [
    controller,
    controller.comparison.data,
    controller.entity.data,
    controller.impact.data,
    controller.ownership.data,
    controller.references.data,
    layer,
    loadedEntity,
    onNavigateEntity,
    recordKey,
    stableRecord
  ]);
}

function InspectorCompare({
  controller,
  layer,
  record
}: Pick<SemanticInspectorTabsOptions, 'controller' | 'layer'> & {
  record: SemanticExploreRecordRef;
}) {
  const { t } = useLocalization();
  useEffect(() => {
    if (layer !== 'base') {
      void controller.compare({ left: 'base', record, right: layer });
    }
  }, [controller.compare, layer, record]);
  if (layer === 'base') {
    return <p className="km-workbench-empty">{t('semanticExplore.inspector.baseOnly')}</p>;
  }
  return (
    <InspectorBoundary state={controller.comparison}>
      <InspectorCoverage coverage={controller.comparison.data?.coverage ?? []} />
      <ul className="km-semantic-inspector-list">
        {controller.comparison.data?.items.map((difference, index) => (
          <li key={`${difference.fieldKey}:${index}`}>
            <span data-localization-ignore="true">
              <strong>{difference.label}</strong>
              <small>{difference.left?.displayValue ?? t('semanticExplore.value.unavailable')}</small>
              <small>{difference.right?.displayValue ?? t('semanticExplore.value.unavailable')}</small>
            </span>
            <em>{t(`semanticExplore.difference.${difference.kind}`)}</em>
          </li>
        ))}
      </ul>
      <InspectorLoadMore
        count={controller.comparison.data?.items.length ?? 0}
        isBusy={controller.comparison.isAppending}
        nextCursor={controller.comparison.data?.nextCursor ?? null}
        onLoad={() => void controller.loadMoreComparison()}
      />
    </InspectorBoundary>
  );
}

function InspectorReferences({
  controller,
  layer,
  onNavigateEntity,
  record
}: SemanticInspectorTabsOptions & { record: SemanticExploreRecordRef }) {
  const { t } = useLocalization();
  const [direction, setDirection] = useState<'incoming' | 'outgoing'>('incoming');
  useEffect(() => {
    void controller.getReferences({ direction, layer, record });
  }, [controller.getReferences, direction, layer, record]);
  return (
    <>
      <div className="km-semantic-inspector-toggle">
        {(['incoming', 'outgoing'] as const).map((value) => (
          <button
            aria-pressed={direction === value}
            className="secondary-button compact-button"
            key={value}
            onClick={() => setDirection(value)}
            type="button"
          >
            {t(`semanticExplore.references.${value}`)}
          </button>
        ))}
      </div>
      <InspectorBoundary state={controller.references}>
        <InspectorCoverage coverage={controller.references.data?.coverage ?? []} />
        <ul className="km-semantic-inspector-list">
          {controller.references.data?.items.map((reference, index) => {
            const relatedRecord = direction === 'incoming' ? reference.source : reference.target;
            return (
              <li key={`${reference.relationshipKey}:${index}`}>
                <span data-localization-ignore="true">
                  <strong>
                    {direction === 'incoming' ? reference.sourceTitle : reference.targetTitle}
                  </strong>
                  <small>{reference.relationshipLabel}</small>
                  <small>{semanticInspectorRecordIdentity(relatedRecord)}</small>
                </span>
                <small>
                  {t(`semanticExplore.coverage.confidence.${reference.confidence}`)}
                </small>
                <button
                  aria-label={`${t('semanticExplore.entity.openEditor')}: ${semanticInspectorRecordIdentity(relatedRecord)}`}
                  className="secondary-button compact-button"
                  onClick={() => onNavigateEntity(relatedRecord)}
                  type="button"
                >
                  {t('semanticExplore.entity.openEditor')}
                </button>
              </li>
            );
          })}
        </ul>
        <InspectorLoadMore
          count={controller.references.data?.items.length ?? 0}
          isBusy={controller.references.isAppending}
          nextCursor={controller.references.data?.nextCursor ?? null}
          onLoad={() => void controller.loadMoreReferences()}
        />
      </InspectorBoundary>
    </>
  );
}

function InspectorImpact({
  controller,
  layer,
  record
}: Pick<SemanticInspectorTabsOptions, 'controller' | 'layer'> & {
  record: SemanticExploreRecordRef;
}) {
  const { t } = useLocalization();
  useEffect(() => {
    void controller.getImpact(record, layer);
  }, [controller.getImpact, layer, record]);
  return (
    <InspectorBoundary state={controller.impact}>
      <InspectorCoverage coverage={controller.impact.data?.coverage ?? []} />
      <p className="km-semantic-advisory">{t('semanticExplore.impact.readOnly')}</p>
      <ul className="km-semantic-inspector-list">
        {controller.impact.data?.items.map((impact) => (
          <li key={`${impact.sourceDomain}:${impact.relationshipKey}`}>
            <span data-localization-ignore="true">
              <strong>{impact.summary}</strong>
              <small data-localization-ignore="true">
                {humanizeIdentifier(impact.sourceDomain)}
              </small>
            </span>
            <em data-localization-ignore="true">{impact.count}</em>
          </li>
        ))}
      </ul>
      <InspectorLoadMore
        count={controller.impact.data?.items.length ?? 0}
        isBusy={controller.impact.isAppending}
        nextCursor={controller.impact.data?.nextCursor ?? null}
        onLoad={() => void controller.loadMoreImpact()}
      />
    </InspectorBoundary>
  );
}

function InspectorOwnership({
  controller,
  onNavigateEntity,
  record
}: Pick<SemanticInspectorTabsOptions, 'controller' | 'onNavigateEntity'> & {
  record: SemanticExploreRecordRef;
}) {
  const { t } = useLocalization();
  useEffect(() => {
    void controller.getOwnership(record);
  }, [controller.getOwnership, record]);
  return (
    <InspectorBoundary state={controller.ownership}>
      <InspectorCoverage coverage={controller.ownership.data?.coverage ?? []} />
      <ul className="km-semantic-inspector-list">
        {controller.ownership.data?.nodes.map((node) => (
          <li key={node.nodeId}>
            <span data-localization-ignore="true">
              <strong>{node.label}</strong>
              {node.record ? <small>{semanticInspectorRecordIdentity(node.record)}</small> : null}
            </span>
            <small>{t(`semanticExplore.ownership.node.${node.kind}`)}</small>
            {node.record ? (
              <button
                aria-label={`${t('semanticExplore.entity.openEditor')}: ${node.label}, ${semanticInspectorRecordIdentity(node.record)}`}
                className="secondary-button compact-button"
                onClick={() => onNavigateEntity(node.record!)}
                type="button"
              >
                {t('semanticExplore.entity.openEditor')}
              </button>
            ) : null}
          </li>
        ))}
      </ul>
      {controller.ownership.data?.conflicts.length ? (
        <p className="km-semantic-query-error" role="status">
          {t('semanticExplore.ownership.conflictCount', {
            count: controller.ownership.data.conflicts.length
          })}
        </p>
      ) : null}
      <InspectorLoadMore
        count={controller.ownership.data?.nodes.length ?? 0}
        isBusy={controller.ownership.isAppending}
        nextCursor={controller.ownership.data?.nextCursor ?? null}
        onLoad={() => void controller.loadMoreOwnership()}
      />
    </InspectorBoundary>
  );
}

function InspectorCoverage({ coverage }: { coverage: readonly SemanticExploreCoverage[] }) {
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

function InspectorBoundary<T>({
  children,
  state
}: {
  children: ReactNode;
  state: SemanticQueryState<T>;
}) {
  const { t } = useLocalization();
  const errorMessage = state.status === 'error'
    ? t(`semanticExplore.query.error.${state.error ?? 'generic'}`)
    : null;
  usePublishCommonEditorError({ domain: 'analysis.semanticExplore', message: errorMessage });
  if ((state.status === 'loading' || state.status === 'idle') && !state.data) {
    return <LoadingProgress label={t('semanticExplore.loading')} />;
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

function InspectorLoadMore({
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
        className="secondary-button compact-button"
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

function matchingRecordCount(
  differences: readonly { record: SemanticExploreRecordRef }[],
  record: SemanticExploreRecordRef
) {
  const key = semanticRecordRefKey(record);
  return differences.filter((difference) => semanticRecordRefKey(difference.record) === key).length;
}

function semanticInspectorRecordIdentity(record: SemanticExploreRecordRef) {
  return [
    record.gameFamily,
    record.domain,
    `${record.recordKind.key}@${record.recordKind.schemaVersion}`,
    record.recordId,
    record.subrecordId
  ].filter(Boolean).join(' / ');
}
