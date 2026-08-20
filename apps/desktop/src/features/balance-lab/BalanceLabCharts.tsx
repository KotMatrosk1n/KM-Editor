/* SPDX-License-Identifier: GPL-3.0-only */

import type { CSSProperties } from 'react';
import type { BalanceLabFact, BalanceLabPoint } from '../../bridge/balanceLabContracts';
import { useLocalization } from '../../localization';

export function BalanceLabChart({
  points,
  study
}: {
  points: readonly BalanceLabPoint[];
  study: 'trainerProgression' | 'encounterDistribution';
}) {
  return study === 'trainerProgression'
    ? <ProgressionChart points={points} />
    : <DistributionChart points={points} />;
}

function ProgressionChart({ points }: { points: readonly BalanceLabPoint[] }) {
  const { t } = useLocalization();
  const values = points
    .filter((point) => point.seriesKey === 'trainer-rank-band')
    .flatMap((point) => {
      const rank = factBySuffix(point.facts, '.fact.royaleRank');
      const level = factBySuffix(point.facts, '.fact.averagePartyLevel') ??
        factBySuffix(point.facts, '.fact.maximumPartyLevel');
      const rankValue = rank ? numericValue(rank) : null;
      const levelValue = level ? numericValue(level) : null;
      return rank && level && rankValue !== null && levelValue !== null
        ? [{ fact: level, point, value: levelValue, xValue: rankValue }]
        : [];
    })
    .sort((left, right) => left.xValue - right.xValue);
  if (values.length === 0) {
    return <ChartUnavailable messageKey="balanceLab.chart.trainerUnavailable" />;
  }
  const width = 760;
  const height = 260;
  const padding = 28;
  const low = Math.min(...values.map((entry) => entry.value));
  const high = Math.max(...values.map((entry) => entry.value));
  const range = high - low || 1;
  const lowX = Math.min(...values.map((entry) => entry.xValue));
  const highX = Math.max(...values.map((entry) => entry.xValue));
  const xRange = highX - lowX || 1;
  const x = (value: number) => padding + (
    (width - padding * 2) * (value - lowX) / xRange
  );
  const y = (value: number) => height - padding - (
    (height - padding * 2) * (value - low) / range
  );
  const path = values.map((entry, index) => (
    `${index === 0 ? 'M' : 'L'} ${x(entry.xValue).toFixed(2)} ${y(entry.value).toFixed(2)}`
  )).join(' ');
  return (
    <figure className="km-balance-chart km-balance-progression-chart">
      <figcaption>{t('balanceLab.chart.trainer')}</figcaption>
      <svg
        aria-label={t('balanceLab.chart.trainerLabel', { count: values.length })}
        preserveAspectRatio="none"
        role="img"
        viewBox={`0 0 ${width} ${height}`}
      >
        <line x1={padding} x2={padding} y1={padding} y2={height - padding} />
        <line
          x1={padding}
          x2={width - padding}
          y1={height - padding}
          y2={height - padding}
        />
        <path className="km-balance-chart-line" d={path} />
        {values.map((entry) => (
          <circle
            cx={x(entry.xValue)}
            cy={y(entry.value)}
            key={entry.point.pointId}
            r="5"
          >
            <title>{`${entry.point.label}: ${entry.fact.value.displayValue}`}</title>
          </circle>
        ))}
      </svg>
      <ChartValueList values={values} />
    </figure>
  );
}

function DistributionChart({ points }: { points: readonly BalanceLabPoint[] }) {
  const { t } = useLocalization();
  const values = points.flatMap((point) => {
    const fact = factBySuffix(point.facts, '.fact.effectiveShare');
    const value = fact ? numericValue(fact) : null;
    return fact && value !== null ? [{ fact, point, value }] : [];
  });
  if (values.length === 0) {
    return <ChartUnavailable messageKey="balanceLab.chart.encounterUnavailable" />;
  }
  const maximum = Math.max(...values.map((entry) => Math.abs(entry.value)), 1);
  return (
    <figure className="km-balance-chart km-balance-distribution-chart">
      <figcaption>{t('balanceLab.chart.encounter')}</figcaption>
      <ul aria-label={t('balanceLab.chart.encounterLabel', { count: values.length })}>
        {values.map((entry) => (
          <li key={entry.point.pointId}>
            <span data-localization-ignore="true">{entry.point.label}</span>
            <span aria-hidden="true" className="km-balance-chart-track">
              <span
                style={{ '--km-balance-bar': `${Math.max(2, Math.abs(entry.value) / maximum * 100)}%` } as CSSProperties}
              />
            </span>
            <strong data-localization-ignore="true">{entry.fact.value.displayValue}</strong>
          </li>
        ))}
      </ul>
    </figure>
  );
}

function ChartValueList({
  values
}: {
  values: readonly {
    fact: BalanceLabFact;
    point: BalanceLabPoint;
    value: number;
  }[];
}) {
  return (
    <ol className="km-balance-chart-values">
      {values.map(({ fact, point }) => (
        <li data-localization-ignore="true" key={point.pointId}>
          <span>{point.label}</span>
          <strong>{fact.value.displayValue}</strong>
        </li>
      ))}
    </ol>
  );
}

function ChartUnavailable({ messageKey }: { messageKey: string }) {
  const { t } = useLocalization();
  return <p className="km-balance-chart-empty">{t(messageKey)}</p>;
}

function factBySuffix(facts: readonly BalanceLabFact[], suffix: string) {
  return facts.find((fact) => fact.factId.endsWith(suffix)) ?? null;
}

function numericValue(fact: BalanceLabFact) {
  if (!fact.value.canonicalValue || ![
    'signedInteger',
    'unsignedInteger',
    'decimal'
  ].includes(fact.value.kind)) {
    return null;
  }
  const value = Number(fact.value.canonicalValue);
  return Number.isFinite(value) ? value : null;
}
