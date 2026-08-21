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
      {study === 'trainerProgression' || study === 'encounterDistribution' ? (
        <>
          <BalanceLabChart points={points} study={study} />
          <PointCards points={points} />
        </>
      ) : (
        <PointCards points={points} />
      )}
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
                  aria-label={t('balanceLab.findings.openLabel', { title: finding.title })}
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
                <strong data-localization-ignore="true">{group.title}</strong>
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
  const groups = (['verified', 'derived', 'unknown'] as const)
    .map((confidence) => ({
      confidence,
      facts: facts.filter((fact) => fact.confidence === confidence)
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
            {group.facts.map((fact) => {
              const value = presentFactValue(
                fact.label,
                fact.value.displayValue,
                fact.unit,
                translateLiteral
              );
              return (
                <div key={fact.factId}>
                  <dt data-localization-ignore="true">{friendlyFactLabel(fact.label, t)}</dt>
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
  return [record.recordId, record.subrecordId].filter(Boolean).join(' / ');
}

function groupPoints(points: readonly BalanceLabPoint[]) {
  const groups = new Map<string, BalanceLabPoint[]>();
  for (const point of points) {
    const key = JSON.stringify([
      point.record.gameFamily,
      point.record.domain,
      point.record.recordKind.key,
      point.record.recordId
    ]);
    const group = groups.get(key);
    if (group) group.push(point);
    else groups.set(key, [point]);
  }
  return [...groups].map(([key, values]) => {
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
      key,
      overview,
      related: values.filter((point) => point !== overview),
      title
    };
  });
}

function friendlyFactLabel(label: string, t: (key: string) => string) {
  const key = presentationFactLabelKey(label);
  return key ? t(key) : label;
}
