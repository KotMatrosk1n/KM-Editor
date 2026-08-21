/* SPDX-License-Identifier: GPL-3.0-only */

import { ArrowDown, ArrowUp, Search } from 'lucide-react';
import { useMemo, useState, type CSSProperties } from 'react';
import type {
  BalanceLabPoint,
  BalanceLabStudy
} from '../../bridge/balanceLabContracts';
import { useLocalization } from '../../localization';
import {
  humanizeIdentifier,
  presentationFactLabelKey
} from '../workbench/analysisPresentationUtils';
import {
  balanceComparisonMetrics,
  balanceComparisonSeries,
  balancePointMetric,
  balanceRecordIdentity,
  comparableBalancePoints,
  defaultBalanceComparisonMetric,
  defaultBalanceComparisonOrder,
  defaultBalanceComparisonSeries,
  orderBalanceComparisonPoints,
  type BalanceComparisonMetric,
  type BalanceComparisonOrder
} from './balanceLabComparison';

type ComparisonDirection = 'ascending' | 'descending';

export function BalanceLabChart({
  points,
  study
}: {
  points: readonly BalanceLabPoint[];
  study: BalanceLabStudy;
}) {
  const { t } = useLocalization();
  const initialSeries = defaultBalanceComparisonSeries(points, study);
  const initialMetrics = balanceComparisonMetrics(points, initialSeries);
  const initialMetricIdentity = defaultBalanceComparisonMetric(initialMetrics, study);
  const initialCandidates = comparableBalancePoints(
    points,
    initialSeries,
    initialMetricIdentity
  );
  const [seriesKey, setSeriesKey] = useState(initialSeries);
  const [metricIdentity, setMetricIdentity] = useState(initialMetricIdentity);
  const [order, setOrder] = useState<BalanceComparisonOrder>(() => (
    defaultBalanceComparisonOrder(initialMetrics, study)
  ));
  const [direction, setDirection] = useState<ComparisonDirection>('ascending');
  const [recordSearch, setRecordSearch] = useState('');
  const [selectedOrder, setSelectedOrder] = useState<readonly string[]>(() => (
    initialSelection(initialCandidates)
  ));

  const series = useMemo(() => balanceComparisonSeries(points), [points]);
  const activeSeries = series.some((candidate) => candidate.key === seriesKey)
    ? seriesKey
    : defaultBalanceComparisonSeries(points, study);
  const metrics = useMemo(
    () => balanceComparisonMetrics(points, activeSeries),
    [activeSeries, points]
  );
  const activeMetricIdentity = metrics.some((metric) => metric.identity === metricIdentity)
    ? metricIdentity
    : defaultBalanceComparisonMetric(metrics, study);
  const candidates = useMemo(
    () => comparableBalancePoints(points, activeSeries, activeMetricIdentity),
    [activeMetricIdentity, activeSeries, points]
  );
  const activeOrder = isAvailableOrder(order, metrics)
    ? order
    : defaultBalanceComparisonOrder(metrics, study);
  const selectedIds = useMemo(() => new Set(selectedOrder), [selectedOrder]);
  const selectedPoints = candidates.filter((point) => selectedIds.has(point.pointId));
  const orderedPoints = orderBalanceComparisonPoints(
    selectedPoints,
    activeOrder,
    direction,
    selectedOrder
  );
  const normalizedRecordSearch = recordSearch.trim().toLocaleLowerCase();
  const visibleCandidates = candidates.filter((point) => (
    !normalizedRecordSearch || [
      point.label,
      balanceRecordIdentity(point),
      balancePointMetric(point, activeMetricIdentity)?.fact.value.displayValue ?? ''
    ].join('\n').toLocaleLowerCase().includes(normalizedRecordSearch)
  ));
  const selectedVisibleCount = visibleCandidates.filter((point) => (
    selectedIds.has(point.pointId)
  )).length;
  const metric = metrics.find((candidate) => (
    candidate.identity === activeMetricIdentity
  )) ?? null;
  const duplicateCandidateLabels = duplicatePointLabels(candidates);

  if (series.length === 0 || !metric) {
    return <ChartUnavailable messageKey={unavailableMessageKey(study)} />;
  }

  const changeSeries = (nextSeries: string) => {
    const nextMetrics = balanceComparisonMetrics(points, nextSeries);
    const nextMetric = defaultBalanceComparisonMetric(nextMetrics, study);
    const nextCandidates = comparableBalancePoints(points, nextSeries, nextMetric);
    setSeriesKey(nextSeries);
    setMetricIdentity(nextMetric);
    setOrder(defaultBalanceComparisonOrder(nextMetrics, study));
    setDirection('ascending');
    setRecordSearch('');
    setSelectedOrder(initialSelection(nextCandidates));
  };
  const changeMetric = (nextMetricIdentity: string) => {
    const nextCandidates = comparableBalancePoints(points, activeSeries, nextMetricIdentity);
    const nextCandidateIds = new Set(nextCandidates.map((point) => point.pointId));
    setMetricIdentity(nextMetricIdentity);
    setSelectedOrder((current) => current.filter((pointId) => nextCandidateIds.has(pointId)));
  };
  const togglePoint = (pointId: string, checked: boolean) => {
    setSelectedOrder((current) => {
      if (checked) return current.includes(pointId) ? current : [...current, pointId];
      return current.filter((candidate) => candidate !== pointId);
    });
  };
  const selectVisible = () => {
    setSelectedOrder((current) => {
      const next = [...current];
      const included = new Set(current);
      for (const point of visibleCandidates) {
        if (included.has(point.pointId)) continue;
        included.add(point.pointId);
        next.push(point.pointId);
      }
      return next;
    });
  };
  const moveSelected = (pointId: string, offset: -1 | 1) => {
    setSelectedOrder((current) => {
      const currentIndex = current.indexOf(pointId);
      const nextIndex = currentIndex + offset;
      if (currentIndex < 0 || nextIndex < 0 || nextIndex >= current.length) return current;
      const next = [...current];
      [next[currentIndex], next[nextIndex]] = [next[nextIndex]!, next[currentIndex]!];
      return next;
    });
  };

  return (
    <figure className="km-balance-chart km-balance-comparison">
      <figcaption>{t(chartTitleKey(study))}</figcaption>
      <p className="km-balance-comparison-description">
        {t('balanceLab.comparison.description')}
      </p>
      <div className="km-balance-comparison-controls">
        <label>
          <span>{t('balanceLab.comparison.series')}</span>
          <select
            className="km-select-control"
            onChange={(event) => changeSeries(event.currentTarget.value)}
            value={activeSeries}
          >
            {series.map((candidate) => (
              <option data-localization-ignore="true" key={candidate.key} value={candidate.key}>
                {humanizeIdentifier(candidate.key)} ({candidate.pointCount.toLocaleString()})
              </option>
            ))}
          </select>
        </label>
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
        <label>
          <span>{t('balanceLab.comparison.order')}</span>
          <select
            className="km-select-control"
            onChange={(event) => setOrder(event.currentTarget.value as BalanceComparisonOrder)}
            value={activeOrder}
          >
            <option value="custom">{t('balanceLab.comparison.order.custom')}</option>
            <option value="label">{t('balanceLab.comparison.order.label')}</option>
            {metrics.map((candidate) => (
              <option
                data-localization-ignore="true"
                key={candidate.identity}
                value={`metric:${candidate.identity}`}
              >
                {t('balanceLab.comparison.order.metric', {
                  metric: metricOptionLabel(candidate, metrics, t)
                })}
              </option>
            ))}
          </select>
        </label>
        {activeOrder !== 'custom' ? (
          <label>
            <span>{t('balanceLab.comparison.direction')}</span>
            <select
              className="km-select-control"
              onChange={(event) => setDirection(event.currentTarget.value as ComparisonDirection)}
              value={direction}
            >
              <option value="ascending">{t('balanceLab.comparison.direction.ascending')}</option>
              <option value="descending">{t('balanceLab.comparison.direction.descending')}</option>
            </select>
          </label>
        ) : null}
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
                autoComplete="off"
                onChange={(event) => setRecordSearch(event.currentTarget.value)}
                placeholder={t('balanceLab.comparison.searchPlaceholder')}
                type="search"
                value={recordSearch}
              />
            </span>
          </label>
          <span aria-live="polite" className="km-balance-selection-count">
            {t('balanceLab.comparison.selected', { count: selectedPoints.length })}
          </span>
          <button
            className="secondary-button compact-button"
            disabled={visibleCandidates.length === 0 || selectedVisibleCount === visibleCandidates.length}
            onClick={selectVisible}
            type="button"
          >
            {t('balanceLab.comparison.selectVisible')}
          </button>
          <button
            className="secondary-button compact-button"
            disabled={selectedPoints.length === 0}
            onClick={() => setSelectedOrder([])}
            type="button"
          >
            {t('balanceLab.comparison.clear')}
          </button>
        </div>
        <p className="km-balance-record-picker-summary">
          {t('balanceLab.comparison.loaded', { count: candidates.length })}
        </p>
        {visibleCandidates.length > 0 ? (
          <div className="km-balance-record-options">
            {visibleCandidates.map((point) => {
              const selectedIndex = selectedOrder.indexOf(point.pointId);
              const isSelected = selectedIndex >= 0;
              return (
                <div className={isSelected ? 'is-selected' : undefined} key={point.pointId}>
                  <label>
                    <input
                      checked={isSelected}
                      className="km-choice-control"
                      onChange={(event) => togglePoint(point.pointId, event.currentTarget.checked)}
                      type="checkbox"
                    />
                    <span>
                      <strong data-localization-ignore="true">{point.label}</strong>
                      <small data-localization-ignore="true">{balanceRecordIdentity(point)}</small>
                    </span>
                  </label>
                  {isSelected && activeOrder === 'custom' ? (
                    <span className="km-balance-record-order-actions">
                      <button
                        aria-label={t('balanceLab.comparison.moveEarlier', {
                          label: comparisonPointLabel(point, duplicateCandidateLabels)
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
                          label: comparisonPointLabel(point, duplicateCandidateLabels)
                        })}
                        className="secondary-button compact-button icon-button"
                        disabled={selectedIndex === selectedOrder.length - 1}
                        onClick={() => moveSelected(point.pointId, 1)}
                        type="button"
                      >
                        <ArrowDown aria-hidden="true" size={14} />
                      </button>
                    </span>
                  ) : null}
                </div>
              );
            })}
          </div>
        ) : (
          <p className="km-balance-chart-empty">{t('balanceLab.comparison.noSearchResults')}</p>
        )}
      </fieldset>

      {orderedPoints.length > 0 ? (
        <ComparisonPlot
          metric={metric}
          points={orderedPoints}
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

function initialSelection(points: readonly BalanceLabPoint[]) {
  return [...points]
    .sort((left, right) => left.label.localeCompare(right.label, undefined, {
      numeric: true,
      sensitivity: 'base'
    }))
    .map((point) => point.pointId);
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

function isAvailableOrder(
  order: BalanceComparisonOrder,
  metrics: readonly BalanceComparisonMetric[]
) {
  return order === 'custom' || order === 'label' || (
    order.startsWith('metric:') &&
    metrics.some((metric) => `metric:${metric.identity}` === order)
  );
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
