/* SPDX-License-Identifier: GPL-3.0-only */

import { ArrowDown, ArrowUp, Plus, Search, X } from 'lucide-react';
import { useMemo, useState, type CSSProperties } from 'react';
import {
  balanceLabMaximumSearchTextLength,
  type BalanceLabPoint,
  type BalanceLabStudy
} from '../../bridge/balanceLabContracts';
import { useLocalization } from '../../localization';
import {
  humanizeIdentifier,
  presentationFactLabelKey
} from '../workbench/analysisPresentationUtils';
import {
  balanceComparisonMetrics,
  balancePointMetric,
  balanceRecordGroupIdentity,
  balanceRecordIdentity,
  comparableBalancePoints,
  defaultBalanceComparisonMetric,
  type BalanceComparisonMetric
} from './balanceLabComparison';

const maximumVisibleSearchResults = 8;

export function BalanceLabChart({
  onSelectedPointIdsChange,
  points,
  selectedPointIds,
  study
}: {
  onSelectedPointIdsChange: (pointIds: readonly string[]) => void;
  points: readonly BalanceLabPoint[];
  selectedPointIds: readonly string[];
  study: BalanceLabStudy;
}) {
  const { t, translateLiteral } = useLocalization();
  const initialMetrics = balanceComparisonMetrics(points);
  const initialMetricIdentity = defaultBalanceComparisonMetric(initialMetrics, study);
  const [metricIdentity, setMetricIdentity] = useState(initialMetricIdentity);
  const [recordSearch, setRecordSearch] = useState('');

  const metrics = useMemo(() => balanceComparisonMetrics(points), [points]);
  const activeMetricIdentity = metrics.some((metric) => metric.identity === metricIdentity)
    ? metricIdentity
    : defaultBalanceComparisonMetric(metrics, study);
  const candidates = useMemo(
    () => comparableBalancePoints(points, activeMetricIdentity),
    [activeMetricIdentity, points]
  );
  const candidateById = useMemo(
    () => new Map(candidates.map((point) => [point.pointId, point])),
    [candidates]
  );
  const recordSearchIndex = useMemo(
    () => buildRecordSearchIndex(points, t, translateLiteral),
    [points, t, translateLiteral]
  );
  const activeSelectedOrder = selectedPointIds.filter((pointId) => candidateById.has(pointId));
  const selectedPoints = activeSelectedOrder.flatMap((pointId) => {
    const point = candidateById.get(pointId);
    return point ? [point] : [];
  });
  const selectedIds = new Set(activeSelectedOrder);
  const normalizedRecordSearch = recordSearch.trim().toLocaleLowerCase();
  const matchingCandidates = normalizedRecordSearch.length === 0 ? [] : candidates
    .filter((point) => (
      recordSearchIndex.get(balanceRecordGroupIdentity(point.record)) ?? ''
    ).includes(normalizedRecordSearch))
    .sort((left, right) => compareSearchMatches(left, right, normalizedRecordSearch));
  const visibleCandidates = matchingCandidates.slice(0, maximumVisibleSearchResults);
  const metric = metrics.find((candidate) => (
    candidate.identity === activeMetricIdentity
  )) ?? null;
  const duplicateCandidateLabels = duplicatePointLabels(candidates);

  if (!metric) {
    return <ChartUnavailable messageKey={unavailableMessageKey(study)} />;
  }

  const changeMetric = (nextMetricIdentity: string) => {
    const nextCandidates = comparableBalancePoints(points, nextMetricIdentity);
    const nextCandidateIds = new Set(nextCandidates.map((point) => point.pointId));
    setMetricIdentity(nextMetricIdentity);
    onSelectedPointIdsChange(
      activeSelectedOrder.filter((pointId) => nextCandidateIds.has(pointId))
    );
  };
  const addPoint = (pointId: string) => {
    if (selectedIds.has(pointId) || !candidateById.has(pointId)) return;
    onSelectedPointIdsChange([...activeSelectedOrder, pointId]);
  };
  const removePoint = (pointId: string) => {
    onSelectedPointIdsChange(activeSelectedOrder.filter((candidate) => candidate !== pointId));
  };
  const moveSelected = (pointId: string, offset: -1 | 1) => {
    const currentIndex = activeSelectedOrder.indexOf(pointId);
    const nextIndex = currentIndex + offset;
    if (currentIndex < 0 || nextIndex < 0 || nextIndex >= activeSelectedOrder.length) return;
    const next = [...activeSelectedOrder];
    [next[currentIndex], next[nextIndex]] = [next[nextIndex]!, next[currentIndex]!];
    onSelectedPointIdsChange(next);
  };

  return (
    <figure className="km-balance-chart km-balance-comparison">
      <figcaption>{t(chartTitleKey(study))}</figcaption>
      <p className="km-balance-comparison-description">
        {t('balanceLab.comparison.description')}
      </p>
      <div className="km-balance-comparison-controls">
        <label>
          <span>{t('balanceLab.comparison.metric')}</span>
          <select
            className="km-select-control"
            onChange={(event) => changeMetric(event.currentTarget.value)}
            value={activeMetricIdentity}
          >
            {metrics.map((candidate) => (
              <option
                data-localization-ignore="true"
                key={candidate.identity}
                value={candidate.identity}
              >
                {metricOptionLabel(candidate, metrics, t)} ({candidate.supportCount.toLocaleString()})
              </option>
            ))}
          </select>
        </label>
      </div>

      <fieldset className="km-balance-record-picker">
        <legend>{t('balanceLab.comparison.records')}</legend>
        <div className="km-balance-record-picker-toolbar">
          <label className="km-balance-record-search">
            <span className="km-workbench-visually-hidden">
              {t('balanceLab.comparison.search')}
            </span>
            <span>
              <Search aria-hidden="true" size={15} />
              <input
                aria-controls={visibleCandidates.length > 0
                  ? 'balance-lab-record-search-results'
                  : undefined}
                autoComplete="off"
                maxLength={balanceLabMaximumSearchTextLength}
                onChange={(event) => setRecordSearch(event.currentTarget.value)}
                onKeyDown={(event) => {
                  if (event.key !== 'Enter') return;
                  const firstAvailable = visibleCandidates.find((point) => (
                    !selectedIds.has(point.pointId)
                  ));
                  if (!firstAvailable) return;
                  event.preventDefault();
                  addPoint(firstAvailable.pointId);
                }}
                placeholder={t('balanceLab.comparison.searchPlaceholder')}
                type="search"
                value={recordSearch}
              />
            </span>
          </label>
          <span aria-live="polite" className="km-balance-selection-count">
            {t('balanceLab.comparison.selected', { count: selectedPoints.length })}
          </span>
        </div>
        {normalizedRecordSearch.length === 0 ? (
          <p className="km-balance-record-picker-summary">
            {t('balanceLab.comparison.searchHint')}
          </p>
        ) : (
          <div className="km-balance-search-results">
            <p aria-live="polite" className="km-balance-record-picker-summary">
              {matchingCandidates.length > visibleCandidates.length
                ? t('balanceLab.comparison.searchResultsLimited', {
                    count: matchingCandidates.length,
                    shown: visibleCandidates.length
                  })
                : t('balanceLab.comparison.searchResults', {
                    count: matchingCandidates.length
                  })}
            </p>
            {visibleCandidates.length > 0 ? (
              <ul id="balance-lab-record-search-results">
                {visibleCandidates.map((point) => {
                  const isSelected = selectedIds.has(point.pointId);
                  const accessibleLabel = comparisonPointLabel(point, duplicateCandidateLabels);
                  return (
                    <li key={point.pointId}>
                      <span>
                        <strong data-localization-ignore="true">{point.label}</strong>
                        <small data-localization-ignore="true">{conciseRecordIdentity(point)}</small>
                      </span>
                      <button
                        aria-label={isSelected
                          ? `${accessibleLabel}: ${t('balanceLab.comparison.alreadySelected')}`
                          : t('balanceLab.comparison.addLabel', { label: accessibleLabel })}
                        className="secondary-button compact-button"
                        disabled={isSelected}
                        onClick={() => addPoint(point.pointId)}
                        type="button"
                      >
                        {isSelected ? (
                          t('balanceLab.comparison.alreadySelected')
                        ) : (
                          <>
                            <Plus aria-hidden="true" size={14} />
                            <span>{t('balanceLab.comparison.add')}</span>
                          </>
                        )}
                      </button>
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="km-balance-chart-empty">{t('balanceLab.comparison.noSearchResults')}</p>
            )}
          </div>
        )}

        {selectedPoints.length > 0 ? (
          <div className="km-balance-selected-records">
            <header>
              <div>
                <strong>{t('balanceLab.comparison.selectedRecords')}</strong>
                <small>{t('balanceLab.comparison.selectionOrderHint')}</small>
              </div>
              <button
                className="secondary-button compact-button"
                onClick={() => onSelectedPointIdsChange([])}
                type="button"
              >
                {t('balanceLab.comparison.clear')}
              </button>
            </header>
            <ol>
              {selectedPoints.map((point, selectedIndex) => {
                const accessibleLabel = comparisonPointLabel(point, duplicateCandidateLabels);
                return (
                  <li key={point.pointId}>
                    <span>
                      <strong data-localization-ignore="true">{point.label}</strong>
                      {duplicateCandidateLabels.has(point.label) ? (
                        <small data-localization-ignore="true">{balanceRecordIdentity(point)}</small>
                      ) : null}
                    </span>
                    <span className="km-balance-record-order-actions">
                      <button
                        aria-label={t('balanceLab.comparison.moveEarlier', {
                          label: accessibleLabel
                        })}
                        className="secondary-button compact-button icon-button"
                        disabled={selectedIndex === 0}
                        onClick={() => moveSelected(point.pointId, -1)}
                        type="button"
                      >
                        <ArrowUp aria-hidden="true" size={14} />
                      </button>
                      <button
                        aria-label={t('balanceLab.comparison.moveLater', {
                          label: accessibleLabel
                        })}
                        className="secondary-button compact-button icon-button"
                        disabled={selectedIndex === selectedPoints.length - 1}
                        onClick={() => moveSelected(point.pointId, 1)}
                        type="button"
                      >
                        <ArrowDown aria-hidden="true" size={14} />
                      </button>
                      <button
                        aria-label={t('balanceLab.comparison.removeLabel', {
                          label: accessibleLabel
                        })}
                        className="secondary-button compact-button icon-button"
                        onClick={() => removePoint(point.pointId)}
                        type="button"
                      >
                        <X aria-hidden="true" size={14} />
                      </button>
                    </span>
                  </li>
                );
              })}
            </ol>
          </div>
        ) : null}
      </fieldset>

      {selectedPoints.length > 0 ? (
        <ComparisonPlot
          metric={metric}
          points={selectedPoints}
          study={study}
        />
      ) : (
        <p className="km-balance-chart-empty">{t('balanceLab.comparison.emptySelection')}</p>
      )}
    </figure>
  );
}

function ComparisonPlot({
  metric,
  points,
  study
}: {
  metric: BalanceComparisonMetric;
  points: readonly BalanceLabPoint[];
  study: BalanceLabStudy;
}) {
  const { t } = useLocalization();
  const values = points.flatMap((point) => {
    const resolved = balancePointMetric(point, metric.identity);
    return resolved ? [{ ...resolved, point }] : [];
  });
  const duplicateLabels = duplicatePointLabels(values.map(({ point }) => point));
  const width = Math.max(720, values.length * 156);
  const height = 284;
  const padding = { bottom: 30, left: 58, right: 24, top: 28 };
  const rawLow = Math.min(...values.map((entry) => entry.value));
  const rawHigh = Math.max(...values.map((entry) => entry.value));
  const isLine = study === 'trainerProgression';
  const rawRange = rawHigh - rawLow;
  const linePadding = rawRange === 0 ? Math.max(Math.abs(rawHigh) * 0.1, 1) : rawRange * 0.08;
  const barLow = Math.min(0, rawLow);
  const barHigh = Math.max(0, rawHigh);
  const low = isLine ? rawLow - linePadding : barLow === barHigh ? -1 : barLow;
  const high = isLine ? rawHigh + linePadding : barLow === barHigh ? 1 : barHigh;
  const range = high - low;
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;
  const x = (index: number) => padding.left + plotWidth * (index + 0.5) / values.length;
  const y = (value: number) => padding.top + plotHeight * (high - value) / range;
  const zeroY = y(Math.min(high, Math.max(low, 0)));
  const path = values.map((entry, index) => (
    `${index === 0 ? 'M' : 'L'} ${x(index).toFixed(2)} ${y(entry.value).toFixed(2)}`
  )).join(' ');
  const ticks = Array.from({ length: 5 }, (_, index) => high - range * index / 4);
  const barWidth = Math.min(84, plotWidth / Math.max(values.length, 1) * 0.58);
  const chartLabel = t('balanceLab.comparison.chartLabel', {
    count: values.length,
    metric: metricLabel(metric, t),
    study: t(`balanceLab.study.${study}`)
  });
  const canvasStyle = {
    '--km-balance-chart-width': `${width}px`,
    '--km-balance-label-left': `${padding.left}px`,
    '--km-balance-label-right': `${padding.right}px`
  } as CSSProperties;

  return (
    <div
      aria-label={t('balanceLab.comparison.scrollLabel')}
      className="km-balance-chart-scroll"
      role="region"
      tabIndex={0}
    >
      <div className="km-balance-chart-canvas" style={canvasStyle}>
        <svg
          aria-label={chartLabel}
          height={height}
          role="img"
          viewBox={`0 0 ${width} ${height}`}
          width={width}
        >
          <title>{chartLabel}</title>
          <desc>{t('balanceLab.comparison.chartDescription')}</desc>
          {ticks.map((tick) => (
            <g className="km-balance-chart-tick" key={tick}>
              <line x1={padding.left} x2={width - padding.right} y1={y(tick)} y2={y(tick)} />
              <text x={padding.left - 8} y={y(tick)}>{formatTick(tick)}</text>
            </g>
          ))}
          <line
            className="km-balance-chart-axis"
            x1={padding.left}
            x2={padding.left}
            y1={padding.top}
            y2={height - padding.bottom}
          />
          <line
            className="km-balance-chart-axis"
            x1={padding.left}
            x2={width - padding.right}
            y1={zeroY}
            y2={zeroY}
          />
          {isLine ? <path className="km-balance-chart-line" d={path} /> : null}
          {values.map((entry, index) => {
            const valueY = y(entry.value);
            const barY = Math.min(valueY, zeroY);
            const barHeight = Math.max(2, Math.abs(zeroY - valueY));
            return isLine ? (
              <circle
                className="km-balance-chart-point"
                cx={x(index)}
                cy={valueY}
                key={entry.point.pointId}
                r="5"
              >
                <title>{`${comparisonPointLabel(entry.point, duplicateLabels)}: ${entry.fact.value.displayValue}`}</title>
              </circle>
            ) : (
              <rect
                className="km-balance-chart-bar"
                height={barHeight}
                key={entry.point.pointId}
                rx="4"
                width={barWidth}
                x={x(index) - barWidth / 2}
                y={barY}
              >
                <title>{`${comparisonPointLabel(entry.point, duplicateLabels)}: ${entry.fact.value.displayValue}`}</title>
              </rect>
            );
          })}
        </svg>
        <ol
          aria-label={chartLabel}
          className="km-balance-chart-values"
          style={{ gridTemplateColumns: `repeat(${values.length}, minmax(0, 1fr))` }}
        >
          {values.map(({ fact, point }) => (
            <li data-localization-ignore="true" key={point.pointId}>
              <span>{point.label}</span>
              {duplicateLabels.has(point.label) ? (
                <small>{balanceRecordIdentity(point)}</small>
              ) : null}
              <strong>{fact.value.displayValue}</strong>
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}

function ChartUnavailable({ messageKey }: { messageKey: string }) {
  const { t } = useLocalization();
  return <p className="km-balance-chart-empty">{t(messageKey)}</p>;
}

function duplicatePointLabels(points: readonly BalanceLabPoint[]) {
  const counts = new Map<string, number>();
  for (const point of points) counts.set(point.label, (counts.get(point.label) ?? 0) + 1);
  return new Set([...counts].filter(([, count]) => count > 1).map(([label]) => label));
}

function comparisonPointLabel(point: BalanceLabPoint, duplicates: ReadonlySet<string>) {
  return duplicates.has(point.label)
    ? `${point.label} (${balanceRecordIdentity(point)})`
    : point.label;
}

function conciseRecordIdentity(point: BalanceLabPoint) {
  return [point.record.recordId, point.record.subrecordId].filter(Boolean).join(' / ');
}

function compareSearchMatches(
  left: BalanceLabPoint,
  right: BalanceLabPoint,
  normalizedSearch: string
) {
  const leftRank = searchMatchRank(left, normalizedSearch);
  const rightRank = searchMatchRank(right, normalizedSearch);
  return leftRank - rightRank ||
    left.label.localeCompare(right.label, undefined, { numeric: true, sensitivity: 'base' }) ||
    balanceRecordIdentity(left).localeCompare(balanceRecordIdentity(right));
}

function buildRecordSearchIndex(
  points: readonly BalanceLabPoint[],
  t: (key: string) => string,
  translateLiteral: (value: string) => string
) {
  const termsByGroup = new Map<string, Set<string>>();
  for (const point of points) {
    const identity = balanceRecordGroupIdentity(point.record);
    const terms = termsByGroup.get(identity) ?? new Set<string>();
    terms.add(point.label);
    terms.add(balanceRecordIdentity(point));
    terms.add(point.record.recordId);
    if (point.record.subrecordId) terms.add(point.record.subrecordId);
    for (const fact of point.facts) {
      terms.add(fact.label);
      const labelKey = presentationFactLabelKey(fact.label);
      if (labelKey) terms.add(t(labelKey));
      terms.add(fact.value.displayValue);
      terms.add(translateLiteral(fact.value.displayValue));
    }
    termsByGroup.set(identity, terms);
  }
  return new Map([...termsByGroup].map(([identity, terms]) => [
    identity,
    [...terms].join('\n').toLocaleLowerCase()
  ]));
}

function searchMatchRank(point: BalanceLabPoint, normalizedSearch: string) {
  const label = point.label.toLocaleLowerCase();
  const recordId = point.record.recordId.toLocaleLowerCase();
  if (label === normalizedSearch || recordId === normalizedSearch) return 0;
  if (label.startsWith(normalizedSearch) || recordId.startsWith(normalizedSearch)) return 1;
  return 2;
}

function metricLabel(
  metric: BalanceComparisonMetric,
  t: (key: string, variables?: Record<string, string | number>) => string
) {
  const labelKey = presentationFactLabelKey(metric.label);
  const label = labelKey ? t(labelKey) : metric.label;
  return metric.unit === null ? label : `${label} (${metric.unit})`;
}

function metricOptionLabel(
  metric: BalanceComparisonMetric,
  metrics: readonly BalanceComparisonMetric[],
  t: (key: string, variables?: Record<string, string | number>) => string
) {
  const label = metricLabel(metric, t);
  const labelCollisions = metrics.filter((candidate) => metricLabel(candidate, t) === label);
  if (labelCollisions.length === 1) return label;

  const keyAwareLabel = `${label} [${humanizeIdentifier(metric.key)}]`;
  const keyCollisions = labelCollisions.filter((candidate) => candidate.key === metric.key);
  return keyCollisions.length === 1
    ? keyAwareLabel
    : `${keyAwareLabel} [${metric.providerId}]`;
}

function chartTitleKey(study: BalanceLabStudy) {
  switch (study) {
    case 'trainerProgression':
      return 'balanceLab.chart.trainer';
    case 'encounterDistribution':
      return 'balanceLab.chart.encounter';
    case 'moveBalance':
      return 'balanceLab.chart.move';
    case 'economy':
      return 'balanceLab.chart.economy';
    case 'pokedexEvolution':
      return 'balanceLab.chart.pokedex';
  }
}

function unavailableMessageKey(study: BalanceLabStudy) {
  return study === 'encounterDistribution'
    ? 'balanceLab.chart.encounterUnavailable'
    : 'balanceLab.chart.comparisonUnavailable';
}

function formatTick(value: number) {
  const maximumFractionDigits = Math.abs(value) < 10 && !Number.isInteger(value) ? 2 : 1;
  return value.toLocaleString(undefined, { maximumFractionDigits });
}
