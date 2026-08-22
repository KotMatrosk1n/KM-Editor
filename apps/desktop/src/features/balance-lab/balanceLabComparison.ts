/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  BalanceLabFact,
  BalanceLabPoint,
  BalanceLabStudy
} from '../../bridge/balanceLabContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';

export type BalanceComparisonMetric = {
  identity: string;
  key: string;
  label: string;
  providerId: string;
  supportCount: number;
  unit: string | null;
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

export function balanceComparisonMetrics(
  points: readonly BalanceLabPoint[]
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

export function comparableBalancePoints(
  points: readonly BalanceLabPoint[],
  metricIdentity: string
) {
  return points.filter((point) => (
    balancePointMetric(point, metricIdentity) !== null
  ));
}

export function balancePointMetric(point: BalanceLabPoint, metricIdentity: string) {
  const fact = point.facts.find((candidate) => (
    balanceMetricIdentity(candidate) === metricIdentity
  )) ?? null;
  const value = fact ? numericFactValue(fact) : null;
  return fact && value !== null ? { fact, value } : null;
}

export function balanceRecordIdentity(point: BalanceLabPoint) {
  return balanceRecordReferenceIdentity(point.record);
}

export function balanceRecordReferenceIdentity(record: SemanticExploreRecordRef) {
  return [
    record.gameFamily,
    record.domain,
    `${record.recordKind.key}@${record.recordKind.schemaVersion}`,
    record.recordId,
    record.subrecordId
  ].filter(Boolean).join(' / ');
}

export function balanceRecordGroupIdentity(record: SemanticExploreRecordRef) {
  return JSON.stringify([
    record.gameFamily,
    record.domain,
    record.recordKind.key,
    record.recordKind.schemaVersion,
    record.recordId
  ]);
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
