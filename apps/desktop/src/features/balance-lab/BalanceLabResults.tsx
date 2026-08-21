/* SPDX-License-Identifier: GPL-3.0-only */

import { ArrowRight, Info, TriangleAlert } from 'lucide-react';
import type {
  BalanceLabConfidence,
  BalanceLabFact,
  BalanceLabFinding,
  BalanceLabPoint,
  BalanceLabStudy
} from '../../bridge/balanceLabContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';
import {
  presentFactValue,
  presentationFactLabelKey,
  relativeRecordTitle,
  humanizeIdentifier
} from '../workbench/analysisPresentationUtils';
import { TechnicalDetails } from '../workbench/AnalysisPresentation';
import { BalanceLabChart } from './BalanceLabCharts';

export function BalanceLabResults({
  findings,
  onNavigateFinding,
  points,
  study
}: {
  findings: readonly BalanceLabFinding[];
  onNavigateFinding: (record: SemanticExploreRecordRef) => void;
  points: readonly BalanceLabPoint[];
  study: BalanceLabStudy;
}) {
  const { t, translateLiteral } = useLocalization();
  return (
    <div className="km-balance-results">
      <BalanceLabChart points={points} study={study} />
      <PointCards points={points} />
      <section aria-labelledby="balance-lab-findings-heading" className="km-balance-findings">
        <header>
          <h3 id="balance-lab-findings-heading">{t('balanceLab.findings.title')}</h3>
          <span>{t('balanceLab.findings.count', { count: findings.length })}</span>
        </header>
        {findings.length > 0 ? (
          <ul>
            {findings.map((finding) => (
              <li data-severity={finding.severity} key={finding.findingId}>
                <div className="km-balance-finding-icon">
                  {finding.severity === 'warning'
                    ? <TriangleAlert aria-hidden="true" size={18} />
                    : <Info aria-hidden="true" size={18} />}
                </div>
                <div>
                  <header>
                    <strong data-localization-ignore="true">{finding.title}</strong>
                    <ConfidenceBadge confidence={finding.confidence} />
                  </header>
                  <p data-localization-ignore="true">{finding.summary}</p>
                  {finding.facts.length > 0 ? <FactList facts={finding.facts} /> : null}
                  <TechnicalDetails summary={translateLiteral('Technical details')}>
                    <code>{recordIdentity(finding.record)}</code>
                    <code>{finding.ruleId}</code>
                  </TechnicalDetails>
                </div>
                <button
                  aria-label={`${t('balanceLab.findings.openLabel', { title: finding.title })}: ${recordIdentity(finding.record)}, ${finding.ruleId}`}
                  className="secondary-button compact-button"
                  data-localization-ignore="true"
                  onClick={() => onNavigateFinding(finding.record)}
                  type="button"
                >
                  <span>{t('balanceLab.findings.open')}</span>
                  <ArrowRight aria-hidden="true" size={15} />
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="km-workbench-empty">{t('balanceLab.findings.empty')}</p>
        )}
      </section>
    </div>
  );
}

function PointCards({ points }: { points: readonly BalanceLabPoint[] }) {
  const { t, translateLiteral } = useLocalization();
  const groups = groupPoints(points);
  return (
    <section aria-labelledby="balance-lab-points-heading" className="km-balance-point-section">
      <h3 id="balance-lab-points-heading">{t('balanceLab.metrics.title')}</h3>
      {points.length > 0 ? (
        <div className="km-balance-point-grid">
          {groups.map((group) => (
            <article key={group.key}>
              <header>
                <strong data-localization-ignore="true">{group.displayTitle}</strong>
              </header>
              {group.overview ? <FactList facts={group.overview.facts} /> : null}
              {group.related.length > 0 ? (
                <div className="km-analysis-related-records">
                  {group.related.map((point) => (
                    <div className="km-analysis-related-record" key={point.pointId}>
                      <header>
                        <strong data-localization-ignore="true">
                          {relativeRecordTitle(point.label, group.title)}
                        </strong>
                      </header>
                      <FactList facts={point.facts} />
                      <TechnicalDetails summary={translateLiteral('Technical details')}>
                        <code>{recordIdentity(point.record)}</code>
                        <code>{point.seriesKey}</code>
                      </TechnicalDetails>
                    </div>
                  ))}
                </div>
              ) : null}
              {group.overview ? (
                <TechnicalDetails summary={translateLiteral('Technical details')}>
                  <code>{recordIdentity(group.overview.record)}</code>
                  <code>{group.overview.seriesKey}</code>
                </TechnicalDetails>
              ) : null}
            </article>
          ))}
        </div>
      ) : (
        <p className="km-workbench-empty">{t('balanceLab.metrics.empty')}</p>
      )}
    </section>
  );
}

function FactList({ facts }: { facts: readonly BalanceLabFact[] }) {
  const { t, translateLiteral } = useLocalization();
  const entries = balanceFactEntries(facts, (label) => friendlyFactLabel(label, t));
  const groups = (['verified', 'derived', 'unknown'] as const)
    .map((confidence) => ({
      confidence,
      facts: entries.filter(({ fact }) => fact.confidence === confidence)
    }))
    .filter((group) => group.facts.length > 0);
  return (
    <div>
      {groups.map((group) => (
        <div className="km-analysis-confidence-group" key={group.confidence}>
          <div className="km-analysis-confidence-heading">
            <ConfidenceBadge confidence={group.confidence} />
            <small>{group.facts.length.toLocaleString()}</small>
          </div>
          <dl className="km-balance-facts">
            {group.facts.map(({ fact, key, label }) => {
              const value = presentFactValue(
                fact.label,
                fact.value.displayValue,
                fact.unit,
                translateLiteral
              );
              return (
                <div key={key}>
                  <dt data-localization-ignore="true">{label}</dt>
                  <dd data-localization-ignore="true">
                    <strong>{value.displayValue}</strong>
                    {value.unit ? <small>{value.unit}</small> : null}
                    {value.changed ? (
                      <TechnicalDetails summary={translateLiteral('Technical details')}>
                        <code>{fact.label}: {value.exactValue}</code>
                      </TechnicalDetails>
                    ) : null}
                  </dd>
                </div>
              );
            })}
          </dl>
        </div>
      ))}
    </div>
  );
}

export function ConfidenceBadge({ confidence }: { confidence: BalanceLabConfidence }) {
  const { t } = useLocalization();
  return (
    <span className="km-balance-confidence" data-confidence={confidence}>
      {t(`balanceLab.confidence.${confidence}`)}
    </span>
  );
}

function recordIdentity(record: SemanticExploreRecordRef) {
  return [
    record.gameFamily,
    record.domain,
    `${record.recordKind.key}@${record.recordKind.schemaVersion}`,
    record.recordId,
    record.subrecordId
  ].filter(Boolean).join(' / ');
}

function groupPoints(points: readonly BalanceLabPoint[]) {
  const groups = new Map<string, BalanceLabPoint[]>();
  for (const point of points) {
    const key = JSON.stringify([
      point.record.gameFamily,
      point.record.domain,
      point.record.recordKind.key,
      point.record.recordKind.schemaVersion,
      point.record.recordId
    ]);
    const group = groups.get(key);
    if (group) group.push(point);
    else groups.set(key, [point]);
  }
  const grouped = [...groups].map(([key, values]) => {
    const overview = values.find((point) => point.record.subrecordId === null) ?? null;
    const firstLabel = values[0]!.label;
    const strippedTitle = firstLabel.replace(
      /(?:,| -)\s*(?:party\s+)?slot\s+\d+$/iu,
      ''
    );
    const title = overview?.label ?? (
      strippedTitle !== firstLabel
        ? strippedTitle
        : `${humanizeIdentifier(values[0]!.record.recordKind.key)} ${values[0]!.record.recordId}`
    );
    return {
      displayTitle: title,
      key,
      overview,
      record: values[0]!.record,
      related: values.filter((point) => point !== overview),
      title
    };
  });
  const titleCounts = new Map<string, number>();
  for (const group of grouped) {
    const key = group.title.toLocaleLowerCase();
    titleCounts.set(key, (titleCounts.get(key) ?? 0) + 1);
  }
  return grouped.map((group) => ({
    ...group,
    displayTitle: (titleCounts.get(group.title.toLocaleLowerCase()) ?? 0) > 1
      ? `${group.title} - ${recordIdentity(group.record)}`
      : group.title
  }));
}

function balanceFactEntries(
  facts: readonly BalanceLabFact[],
  labelFor: (label: string) => string
) {
  const entries = facts.map((fact, index) => ({
    fact,
    index,
    label: labelFor(fact.label)
  }));
  const labelCounts = countLabels(entries.map((entry) => entry.label));
  const withFactId = entries.map((entry) => ({
    ...entry,
    label: (labelCounts.get(entry.label.toLocaleLowerCase()) ?? 0) > 1
      ? `${entry.label} [${entry.fact.factId}]`
      : entry.label
  }));
  const exactCounts = countLabels(withFactId.map((entry) => entry.label));
  const withProvider = withFactId.map((entry) => ({
    ...entry,
    label: (exactCounts.get(entry.label.toLocaleLowerCase()) ?? 0) > 1
      ? `${entry.label} [${entry.fact.providerId}]`
      : entry.label
  }));
  const finalCounts = countLabels(withProvider.map((entry) => entry.label));
  const occurrences = new Map<string, number>();
  return withProvider.map((entry) => {
    const normalized = entry.label.toLocaleLowerCase();
    const occurrence = (occurrences.get(normalized) ?? 0) + 1;
    occurrences.set(normalized, occurrence);
    return {
      fact: entry.fact,
      key: `${entry.fact.factId}:${entry.index}`,
      label: (finalCounts.get(normalized) ?? 0) > 1
        ? `${entry.label} #${occurrence}`
        : entry.label
    };
  });
}

function countLabels(labels: readonly string[]) {
  const counts = new Map<string, number>();
  for (const label of labels) {
    const normalized = label.toLocaleLowerCase();
    counts.set(normalized, (counts.get(normalized) ?? 0) + 1);
  }
  return counts;
}

function friendlyFactLabel(label: string, t: (key: string) => string) {
  const key = presentationFactLabelKey(label);
  return key ? t(key) : label;
}
