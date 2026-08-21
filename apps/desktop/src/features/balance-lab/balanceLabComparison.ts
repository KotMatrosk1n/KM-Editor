/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  BalanceLabFact,
  BalanceLabPoint,
  BalanceLabStudy
} from '../../bridge/balanceLabContracts';

export type BalanceComparisonMetric = {
  identity: string;
  key: string;
  label: string;
  providerId: string;
  supportCount: number;
  unit: string | null;
};

export type BalanceComparisonOrder =
  | 'custom'
  | 'label'
  | `metric:${string}`;

export type BalanceComparisonSeries = {
  key: string;
  pointCount: number;
};

const preferredSeriesByStudy: Record<BalanceLabStudy, readonly string[]> = {
  trainerProgression: ['trainer-rank-band', 'trainer-roster', 'trainer-party'],
  encounterDistribution: ['encounter-slot', 'encounter-table'],
  moveBalance: ['move'],
  economy: ['item-price'],
  pokedexEvolution: ['pokedex']
};

const preferredMetricsByStudy: Record<BalanceLabStudy, readonly string[]> = {
  trainerProgression: [
    'averagePartyLevel',
    'maximumPartyLevel',
    'royaleRank',
    'level',
    'partySize'
  ],
  encounterDistribution: [
    'effectiveShare',
    'nativeWeight',
    'maximumLevel',
    'minimumLevel',
    'slotCount'
  ],
  moveBalance: ['power', 'accuracyValue', 'pp', 'priority', 'runtimeVariantCount'],
  economy: [
    'buyPrice',
    'derivedSellValue',
    'wattsPrice',
    'battlePointPrice',
    'alternatePriceValue'
  ],
  pokedexEvolution: [
    'evolutionCount',
    'regionalDexIndex',
    'paldeaDexIndex',
    'kitakamiDexIndex',
    'blueberryDexIndex',
    'armorDexIndex',
    'crownDexIndex',
    'speciesId'
  ]
};

export function balanceComparisonSeries(
  points: readonly BalanceLabPoint[]
): readonly BalanceComparisonSeries[] {
  const counts = new Map<string, number>();
  for (const point of points) {
    if (!point.facts.some((fact) => numericFactValue(fact) !== null)) continue;
    counts.set(point.seriesKey, (counts.get(point.seriesKey) ?? 0) + 1);
  }
  return [...counts]
    .map(([key, pointCount]) => ({ key, pointCount }))
    .sort((left, right) => left.key.localeCompare(right.key));
}

export function balanceComparisonMetrics(
  points: readonly BalanceLabPoint[],
  seriesKey: string
): readonly BalanceComparisonMetric[] {
  const metrics = new Map<
    string,
    {
      key: string;
      labels: Map<string, number>;
      providerId: string;
      supportCount: number;
      unit: string | null;
    }
  >();
  for (const point of points) {
    if (point.seriesKey !== seriesKey) continue;
    const seen = new Set<string>();
    for (const fact of point.facts) {
      if (numericFactValue(fact) === null) continue;
      const key = balanceFactKey(fact);
      const identity = balanceMetricIdentity(fact);
      if (seen.has(identity)) continue;
      seen.add(identity);
      const metric = metrics.get(identity) ?? {
        key,
        labels: new Map<string, number>(),
        providerId: fact.providerId,
        supportCount: 0,
        unit: fact.unit
      };
      metric.supportCount += 1;
      metric.labels.set(fact.label, (metric.labels.get(fact.label) ?? 0) + 1);
      metrics.set(identity, metric);
    }
  }
  return [...metrics]
    .map(([identity, metric]) => ({
      identity,
      key: metric.key,
      label: mostFrequent(metric.labels) ?? metric.key,
      providerId: metric.providerId,
      supportCount: metric.supportCount,
      unit: metric.unit
    }))
    .sort((left, right) => (
      right.supportCount - left.supportCount ||
      compareText(left.label, right.label) ||
      compareText(left.key, right.key) ||
      compareText(left.unit ?? '', right.unit ?? '') ||
      compareText(left.providerId, right.providerId)
    ));
}

export function defaultBalanceComparisonSeries(
  points: readonly BalanceLabPoint[],
  study: BalanceLabStudy
) {
  const series = balanceComparisonSeries(points);
  return preferredSeriesByStudy[study].find((key) => (
    series.some((candidate) => candidate.key === key)
  )) ?? series[0]?.key ?? '';
}

export function defaultBalanceComparisonMetric(
  metrics: readonly BalanceComparisonMetric[],
  study: BalanceLabStudy
) {
  for (const key of preferredMetricsByStudy[study]) {
    const preferred = metrics.find((metric) => metric.key === key);
    if (preferred) return preferred.identity;
  }
  return metrics[0]?.identity ?? '';
}

export function defaultBalanceComparisonOrder(
  metrics: readonly BalanceComparisonMetric[],
  study: BalanceLabStudy
): BalanceComparisonOrder {
  if (
    study === 'trainerProgression' &&
    metrics.some((metric) => metric.key === 'royaleRank')
  ) {
    const royaleRank = metrics.find((metric) => metric.key === 'royaleRank');
    if (royaleRank) return `metric:${royaleRank.identity}`;
  }
  return 'label';
}

export function comparableBalancePoints(
  points: readonly BalanceLabPoint[],
  seriesKey: string,
  metricIdentity: string
) {
  return points.filter((point) => (
    point.seriesKey === seriesKey && balancePointMetric(point, metricIdentity) !== null
  ));
}

export function balancePointMetric(point: BalanceLabPoint, metricIdentity: string) {
  const fact = point.facts.find((candidate) => (
    balanceMetricIdentity(candidate) === metricIdentity
  )) ?? null;
  const value = fact ? numericFactValue(fact) : null;
  return fact && value !== null ? { fact, value } : null;
}

export function orderBalanceComparisonPoints(
  points: readonly BalanceLabPoint[],
  order: BalanceComparisonOrder,
  direction: 'ascending' | 'descending',
  selectedOrder: readonly string[]
) {
  const directionMultiplier = direction === 'ascending' ? 1 : -1;
  const customPositions = new Map(selectedOrder.map((pointId, index) => [pointId, index]));
  return [...points].sort((left, right) => {
    if (order === 'custom') {
      return (customPositions.get(left.pointId) ?? Number.MAX_SAFE_INTEGER) -
        (customPositions.get(right.pointId) ?? Number.MAX_SAFE_INTEGER);
    }
    if (order === 'label') {
      return directionMultiplier * compareText(left.label, right.label) || compareIdentity(left, right);
    }
    const metricIdentity = order.slice('metric:'.length);
    const leftValue = balancePointMetric(left, metricIdentity)?.value ?? null;
    const rightValue = balancePointMetric(right, metricIdentity)?.value ?? null;
    if (leftValue === null && rightValue === null) return compareIdentity(left, right);
    if (leftValue === null) return 1;
    if (rightValue === null) return -1;
    return directionMultiplier * (leftValue - rightValue) || compareIdentity(left, right);
  });
}

export function balanceRecordIdentity(point: BalanceLabPoint) {
  return [
    point.record.gameFamily,
    point.record.domain,
    `${point.record.recordKind.key}@${point.record.recordKind.schemaVersion}`,
    point.record.recordId,
    point.record.subrecordId
  ].filter(Boolean).join(' / ');
}

function numericFactValue(fact: BalanceLabFact) {
  if (fact.value.canonicalValue === null || ![
    'signedInteger',
    'unsignedInteger',
    'decimal'
  ].includes(fact.value.kind)) {
    return null;
  }
  const value = Number(fact.value.canonicalValue);
  if (!Number.isFinite(value)) return null;
  if (
    (fact.value.kind === 'signedInteger' || fact.value.kind === 'unsignedInteger') &&
    !Number.isSafeInteger(value)
  ) return null;
  return value;
}

function balanceFactKey(fact: BalanceLabFact) {
  const marker = '.fact.';
  const markerIndex = fact.factId.lastIndexOf(marker);
  return markerIndex >= 0 ? fact.factId.slice(markerIndex + marker.length) : fact.factId;
}

function balanceMetricIdentity(fact: BalanceLabFact) {
  return JSON.stringify([fact.providerId, balanceFactKey(fact), fact.unit]);
}

function mostFrequent(values: ReadonlyMap<string, number>) {
  return [...values].sort((left, right) => (
    right[1] - left[1] || left[0].localeCompare(right[0])
  ))[0]?.[0] ?? null;
}

function compareText(left: string, right: string) {
  return left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' });
}

function compareIdentity(left: BalanceLabPoint, right: BalanceLabPoint) {
  return compareText(balanceRecordIdentity(left), balanceRecordIdentity(right));
}
