/* SPDX-License-Identifier: GPL-3.0-only */

import { ArrowRight, Info, TriangleAlert } from 'lucide-react';
import type {
  BalanceLabConfidence,
  BalanceLabFinding,
  BalanceLabPoint,
  BalanceLabStudy
} from '../../bridge/balanceLabContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';
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
  const { t } = useLocalization();
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
                  <small data-localization-ignore="true">
                    {recordIdentity(finding.record)}
                  </small>
                  {finding.facts.length > 0 ? <FactList facts={finding.facts} /> : null}
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
  const { t } = useLocalization();
  return (
    <section aria-labelledby="balance-lab-points-heading" className="km-balance-point-section">
      <h3 id="balance-lab-points-heading">{t('balanceLab.metrics.title')}</h3>
      {points.length > 0 ? (
        <div className="km-balance-point-grid">
          {points.map((point) => (
            <article key={point.pointId}>
              <header>
                <strong data-localization-ignore="true">{point.label}</strong>
                <small data-localization-ignore="true">{point.seriesKey}</small>
              </header>
              <FactList facts={point.facts} />
            </article>
          ))}
        </div>
      ) : (
        <p className="km-workbench-empty">{t('balanceLab.metrics.empty')}</p>
      )}
    </section>
  );
}

function FactList({ facts }: { facts: BalanceLabPoint['facts'] }) {
  return (
    <dl className="km-balance-facts">
      {facts.map((fact) => (
        <div key={fact.factId}>
          <dt data-localization-ignore="true">{fact.label}</dt>
          <dd data-localization-ignore="true">
            <strong>{fact.value.displayValue}</strong>
            {fact.unit ? <small>{fact.unit}</small> : null}
            <ConfidenceBadge confidence={fact.confidence} />
          </dd>
        </div>
      ))}
    </dl>
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
