/* SPDX-License-Identifier: GPL-3.0-only */

import { BarChart3, RefreshCw, Search, ShieldCheck } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import type {
  BalanceLabCapability,
  BalanceLabConfidence,
  BalanceLabFinding,
  BalanceLabPoint,
  BalanceLabStudy
} from '../../bridge/balanceLabContracts';
import type { ApiDiagnostic } from '../../bridge/contracts';
import {
  balanceLabMaximumContinuationStartCount,
  balanceLabMaximumSearchTextLength
} from '../../bridge/balanceLabContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useDiagnosticNavigation } from '../../diagnosticActions';
import { formatDiagnosticSummary } from '../../diagnostics';
import { useLocalization } from '../../localization';
import {
  DiagnosticTechnicalDetails,
  OccurrenceCount,
  TechnicalDetails
} from '../workbench/AnalysisPresentation';
import {
  diagnosticSeverityPriority,
  groupDiagnosticsForPresentation,
  presentationDiagnosticMessage,
  presentationDiagnosticSeverity
} from '../workbench/analysisPresentationUtils';
import { BalanceLabResults, ConfidenceBadge } from './BalanceLabResults';
import type { BalanceLabController, BalanceLabLayer } from './useBalanceLabController';
import './balanceLab.css';

export type BalanceLabSectionProps = {
  availableLayers?: readonly BalanceLabLayer[];
  controller: BalanceLabController;
  onNavigateFinding: (record: SemanticExploreRecordRef) => void;
};

type ConfidenceFilter = BalanceLabConfidence | 'all';
type SeverityFilter = BalanceLabFinding['severity'] | 'all';

export function BalanceLabSection({
  availableLayers = ['base'],
  controller,
  onNavigateFinding
}: BalanceLabSectionProps) {
  const { t } = useLocalization();
  const layers = useMemo(
    () => [...new Set(availableLayers.length > 0 ? availableLayers : ['base'] as const)],
    [availableLayers]
  );
  const [study, setStudy] = useState<BalanceLabStudy>('trainerProgression');
  const [layer, setLayer] = useState<BalanceLabLayer>(layers[0] ?? 'base');
  const [searchText, setSearchText] = useState('');
  const [confidence, setConfidence] = useState<ConfidenceFilter>('all');
  const [severity, setSeverity] = useState<SeverityFilter>('all');
  const resultMatchesSelection = controller.activeQuery?.study === study &&
    controller.activeQuery.layer === layer;
  const resultData = resultMatchesSelection ? controller.result.data : null;
  const capabilities = resultData?.capabilities ?? [];
  const supportedStudies = capabilities.filter((item) => item.state !== 'unavailable');
  const selectedCapability = capabilities.find((item) => item.study === study) ?? null;

  useEffect(() => {
    if (!layers.includes(layer)) setLayer(layers[0] ?? 'base');
  }, [layer, layers]);

  useEffect(() => {
    if (
      controller.activeQuery?.study !== study ||
      controller.activeQuery.layer !== layer
    ) {
      void controller.query({ layer, study });
    }
  }, [controller.activeQuery, controller.query, layer, study]);

  const normalizedSearch = searchText.trim().toLocaleLowerCase();
  const points = filterPoints(
    resultData?.points ?? [],
    normalizedSearch,
    confidence
  );
  const findings = filterFindings(
    resultData?.findings ?? [],
    normalizedSearch,
    confidence,
    severity
  );

  return (
    <section
      aria-busy={controller.isQuerying || undefined}
      aria-labelledby="balance-lab-title"
      className="km-balance-lab wide-panel"
    >
      <header className="km-balance-heading">
        <div>
          <p>{t('balanceLab.eyebrow')}</p>
          <h2 id="balance-lab-title">{t('balanceLab.title')}</h2>
          <span>{t('balanceLab.description')}</span>
        </div>
        <button
          aria-busy={controller.isQuerying || undefined}
          className="secondary-button compact-button"
          disabled={!controller.activeQuery || controller.isQuerying}
          onClick={() => void controller.refresh()}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={15} />
          <span>{t(controller.isQuerying ? 'balanceLab.loading' : 'balanceLab.refresh')}</span>
        </button>
      </header>

      {(!resultMatchesSelection || controller.result.status === 'loading') && !resultData ? (
        <BalanceLabStatusPanel kind="loading" />
      ) : null}
      {resultMatchesSelection && controller.result.status === 'error' && !resultData ? (
        <BalanceLabStatusPanel kind="error" onRetry={() => void controller.refresh()} />
      ) : null}
      {resultData && controller.result.status === 'loading' && !controller.result.isAppending ? (
        <div className="km-balance-status">
          <LoadingProgress className="is-compact" label={t('balanceLab.loading')} />
        </div>
      ) : null}

      {resultData ? (
        <>
          <CapabilitySummary capabilities={capabilities} />
          {supportedStudies.length > 0 ? (
            <StudyTabs
              activeStudy={study}
              capabilities={supportedStudies}
              onChange={setStudy}
            />
          ) : null}
          <div
            aria-label={supportedStudies.some((item) => item.study === study)
              ? undefined
              : t(`balanceLab.study.${study}`)}
            aria-labelledby={supportedStudies.some((item) => item.study === study)
              ? `balance-lab-study-${study}`
              : undefined}
            id="balance-lab-study-panel"
            role="tabpanel"
          >
          {controller.result.status === 'error' ? (
            <InlineError onRetry={() => void controller.refresh()} />
          ) : null}
          <DiagnosticList diagnostics={resultData.diagnostics} />
          {selectedCapability && selectedCapability.state !== 'unavailable' ? (
            <>
              <div className="km-balance-controls">
                <label className="km-balance-search">
                  <span>{t('balanceLab.filters.search')}</span>
                  <span>
                    <Search aria-hidden="true" size={16} />
                    <input
                      autoComplete="off"
                      maxLength={balanceLabMaximumSearchTextLength}
                      onChange={(event) => setSearchText(event.currentTarget.value)}
                      placeholder={t('balanceLab.filters.searchPlaceholder')}
                      type="search"
                      value={searchText}
                    />
                  </span>
                </label>
                <SelectControl
                  labelKey="balanceLab.filters.layer"
                  onChange={(value) => setLayer(value as BalanceLabLayer)}
                  options={layers.map((value) => ({
                    labelKey: `balanceLab.layer.${value}`,
                    value
                  }))}
                  value={layer}
                />
                <SelectControl
                  labelKey="balanceLab.filters.confidence"
                  onChange={(value) => setConfidence(value as ConfidenceFilter)}
                  options={['all', 'verified', 'derived', 'unknown'].map((value) => ({
                    labelKey: `balanceLab.confidence.${value}`,
                    value
                  }))}
                  value={confidence}
                />
                <SelectControl
                  labelKey="balanceLab.filters.severity"
                  onChange={(value) => setSeverity(value as SeverityFilter)}
                  options={['all', 'warning', 'info'].map((value) => ({
                    labelKey: `balanceLab.severity.${value}`,
                    value
                  }))}
                  value={severity}
                />
              </div>

              <BalanceLabResults
                findings={findings}
                key={resultData.queryFingerprint}
                onNavigateFinding={onNavigateFinding}
                points={points}
                study={study}
              />
              <ResultWindowSummary
                filteredFindings={findings.length}
                filteredPoints={points.length}
                totalFindings={resultData.findings.length}
                totalPoints={resultData.points.length}
              />
              {resultData.nextCursor ? (
                resultData.points.length + resultData.findings.length >=
                  balanceLabMaximumContinuationStartCount ? (
                  <p className="km-balance-advisory">{t('balanceLab.results.windowLimit')}</p>
                ) : (
                  <>
                    <button
                      aria-busy={controller.result.isAppending || undefined}
                      className="secondary-button km-balance-load-more"
                      disabled={controller.result.isAppending}
                      onClick={() => void controller.loadMore()}
                      type="button"
                    >
                      {controller.result.isAppending
                        ? t('balanceLab.loading')
                        : t('balanceLab.results.more')}
                    </button>
                    {controller.result.isAppending ? (
                      <LoadingProgress className="is-compact" label={t('balanceLab.loading')} />
                    ) : null}
                  </>
                )
              ) : null}
            </>
          ) : (
            <p className="km-workbench-empty">{t('balanceLab.studyUnavailable')}</p>
          )}
          </div>
        </>
      ) : null}
    </section>
  );
}

function CapabilitySummary({ capabilities }: { capabilities: readonly BalanceLabCapability[] }) {
  const { t, translateLiteral } = useLocalization();
  return (
    <section aria-labelledby="balance-lab-coverage-title" className="km-balance-coverage">
      <header>
        <ShieldCheck aria-hidden="true" size={18} />
        <div>
          <h3 id="balance-lab-coverage-title">{t('balanceLab.coverage.title')}</h3>
          <p>{t('balanceLab.coverage.disclaimer')}</p>
        </div>
      </header>
      <ul>
        {capabilities.map((capability) => (
          <li key={capability.study}>
            <strong>{t(`balanceLab.study.${capability.study}`)}</strong>
            <span className="km-balance-coverage-state" data-state={capability.state}>
              {t(`balanceLab.coverage.${capability.state}`)}
            </span>
            <ConfidenceBadge confidence={capability.confidence} />
            {capability.reasonCode ? (
              <div className="km-balance-coverage-reason">
                <span>{t(coverageReasonKey(capability.reasonCode))}</span>
                <TechnicalDetails summary={translateLiteral('Technical details')}>
                  <code>{capability.reasonCode}</code>
                </TechnicalDetails>
              </div>
            ) : null}
          </li>
        ))}
      </ul>
    </section>
  );
}

function StudyTabs({
  activeStudy,
  capabilities,
  onChange
}: {
  activeStudy: BalanceLabStudy;
  capabilities: readonly BalanceLabCapability[];
  onChange: (study: BalanceLabStudy) => void;
}) {
  const { t } = useLocalization();
  const hasActiveStudy = capabilities.some((capability) => capability.study === activeStudy);
  return (
    <div aria-label={t('balanceLab.studies.label')} className="km-balance-study-tabs" role="tablist">
      {capabilities.map((capability, index) => (
        <button
          aria-controls="balance-lab-study-panel"
          aria-selected={activeStudy === capability.study}
          className={activeStudy === capability.study ? 'is-active' : undefined}
          id={`balance-lab-study-${capability.study}`}
          key={capability.study}
          onClick={() => onChange(capability.study)}
          onKeyDown={(event) => {
            const next = tabIndex(event.key, index, capabilities.length);
            if (next === null) return;
            event.preventDefault();
            const capability = capabilities[next];
            if (!capability) return;
            onChange(capability.study);
            event.currentTarget.parentElement
              ?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]
              ?.focus({ preventScroll: true });
          }}
          role="tab"
          tabIndex={
            activeStudy === capability.study || (!hasActiveStudy && index === 0) ? 0 : -1
          }
          type="button"
        >
          <BarChart3 aria-hidden="true" size={16} />
          <span>{t(`balanceLab.study.${capability.study}`)}</span>
        </button>
      ))}
    </div>
  );
}

function SelectControl({
  labelKey,
  onChange,
  options,
  value
}: {
  labelKey: string;
  onChange: (value: string) => void;
  options: readonly { labelKey: string; value: string }[];
  value: string;
}) {
  const { t } = useLocalization();
  return (
    <label>
      <span>{t(labelKey)}</span>
      <select
        className="km-select-control"
        onChange={(event) => onChange(event.currentTarget.value)}
        value={value}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>{t(option.labelKey)}</option>
        ))}
      </select>
    </label>
  );
}

function DiagnosticList({ diagnostics }: { diagnostics: readonly ApiDiagnostic[] }) {
  const { t, translateLiteral } = useLocalization();
  const diagnosticNavigation = useDiagnosticNavigation();
  if (diagnostics.length === 0) return null;
  const formatMessage = (diagnostic: ApiDiagnostic) => (
    safeDiagnosticMessage(diagnostic.message)
      ? formatDiagnosticSummary(diagnostic, translateLiteral, t)
      : t('balanceLab.diagnostics.redacted')
  );
  const presentedMessage = (diagnostic: ApiDiagnostic) => (
    presentationDiagnosticMessage(diagnostic, diagnostics, formatMessage)
  );
  const presentedSeverity = (diagnostic: ApiDiagnostic) => (
    presentationDiagnosticSeverity(diagnostic, diagnostics, formatMessage)
  );
  const grouped = groupDiagnosticsForPresentation(
    diagnostics,
    (diagnostic) => [presentedSeverity(diagnostic), presentedMessage(diagnostic)],
    (diagnostic) => [
      diagnostic.severity,
      diagnostic.code,
      diagnostic.domain,
      diagnostic.field
    ],
    (diagnostic) => diagnosticSeverityPriority(presentedSeverity(diagnostic))
  );
  const primaryAction = [...diagnostics]
    .sort((left, right) => (
      diagnosticSeverityPriority(right.severity) - diagnosticSeverityPriority(left.severity)
    ))
    .map((diagnostic) => diagnosticNavigation.resolveAction(diagnostic))
    .find((action) => action !== null);
  return (
    <section aria-label={t('balanceLab.diagnostics.title')} className="km-balance-diagnostics">
      {primaryAction ? (
        <div className="km-analysis-diagnostic-action">
          <button
            className="secondary-button compact-button"
            onClick={() => diagnosticNavigation.navigate(primaryAction.location)}
            type="button"
          >
            {t('diagnostics.openAction', {
              target: translateLiteral(primaryAction.targetLabel)
            })}
          </button>
        </div>
      ) : null}
      <ul>
        {grouped.slice(0, 50).map(({ count, diagnostics: identities, key }) => {
          const diagnostic = identities[0]!.diagnostic;
          return (
          <li data-severity={presentedSeverity(diagnostic)} key={key}>
            <span>
              <span>{presentedMessage(diagnostic)}</span>
              <OccurrenceCount count={count} />
            </span>
            <DiagnosticTechnicalDetails
              diagnostics={identities}
              summary={translateLiteral('Technical details')}
            />
          </li>
          );
        })}
      </ul>
      {grouped.length > 50 ? <p>{t('balanceLab.diagnostics.bounded')}</p> : null}
    </section>
  );
}

function ResultWindowSummary({
  filteredFindings,
  filteredPoints,
  totalFindings,
  totalPoints
}: {
  filteredFindings: number;
  filteredPoints: number;
  totalFindings: number;
  totalPoints: number;
}) {
  const { t } = useLocalization();
  return (
    <p aria-live="polite" className="km-balance-window-summary">
      {t('balanceLab.results.summary', {
        filteredFindings,
        filteredPoints,
        totalFindings,
        totalPoints
      })}
    </p>
  );
}

export function BalanceLabStatusPanel({
  kind,
  messageKey,
  onRetry
}: {
  kind: 'loading' | 'error';
  messageKey?: string;
  onRetry?: () => void;
}) {
  const { t } = useLocalization();
  const label = t(messageKey ?? (kind === 'loading' ? 'balanceLab.loading' : 'balanceLab.error'));
  if (kind === 'loading') {
    return (
      <div className="km-balance-status">
        <LoadingProgress label={label} />
      </div>
    );
  }
  return (
    <div aria-live="polite" className="km-balance-status" role="alert">
      <p>{label}</p>
      {onRetry ? <button onClick={onRetry} type="button">{t('balanceLab.retry')}</button> : null}
    </div>
  );
}

function InlineError({ onRetry }: { onRetry: () => void }) {
  const { t } = useLocalization();
  return (
    <div className="km-balance-inline-error" role="alert">
      <span>{t('balanceLab.query.error')}</span>
      <button className="secondary-button compact-button" onClick={onRetry} type="button">
        {t('balanceLab.retry')}
      </button>
    </div>
  );
}

function filterPoints(
  points: readonly BalanceLabPoint[],
  searchText: string,
  confidence: ConfidenceFilter
) {
  return points.filter((point) => {
    const matchesConfidence = confidence === 'all' || point.facts.some(
      (fact) => fact.confidence === confidence
    );
    const haystack = [
      point.label,
      point.seriesKey,
      ...point.facts.flatMap((fact) => [fact.label, fact.value.displayValue])
    ].join('\n').toLocaleLowerCase();
    return matchesConfidence && (!searchText || haystack.includes(searchText));
  });
}

function filterFindings(
  findings: readonly BalanceLabFinding[],
  searchText: string,
  confidence: ConfidenceFilter,
  severity: SeverityFilter
) {
  return findings.filter((finding) => {
    const matchesConfidence = confidence === 'all' || finding.confidence === confidence;
    const matchesSeverity = severity === 'all' || finding.severity === severity;
    const haystack = [
      finding.title,
      finding.summary,
      finding.ruleId,
      ...finding.facts.flatMap((fact) => [fact.label, fact.value.displayValue])
    ].join('\n').toLocaleLowerCase();
    return matchesConfidence && matchesSeverity && (!searchText || haystack.includes(searchText));
  });
}

function safeDiagnosticMessage(message: string) {
  return !(
    /(?:^|[^a-z])[a-z]:[\\/]/iu.test(message) ||
    /\\\\[^\s]+\\[^\s]+/u.test(message) ||
    /\/(?:users|home|var|tmp|mnt)\//iu.test(message)
  );
}

function coverageReasonKey(reasonCode: string) {
  switch (reasonCode) {
    case 'progression-order-and-move-legality-unavailable':
    case 'move-legality-and-full-story-order-unavailable':
      return 'balanceLab.coverage.reason.trainerLimits';
    case 'story-phase-and-placement-coverage-unavailable':
    case 'eligibility-filters-population-caps-and-coordinates-unavailable':
      return 'balanceLab.coverage.reason.encounterLimits';
    case 'move-consumer-coverage-unavailable':
      return 'balanceLab.coverage.reason.moveLimits';
    case 'acquisition-and-reward-coverage-unavailable':
      return 'balanceLab.coverage.reason.economyLimits';
    case 'overall-obtainability-coverage-unavailable':
      return 'balanceLab.coverage.reason.pokedexLimits';
    case 'pending-overlay-unavailable':
      return 'balanceLab.coverage.reason.pendingUnavailable';
    case 'workflow-disabled':
      return 'balanceLab.coverage.reason.workflowDisabled';
    case 'workflow-source-invalid':
      return 'balanceLab.coverage.reason.sourceInvalid';
    case 'workflow-source-unavailable':
      return 'balanceLab.coverage.reason.sourceUnavailable';
    default:
      return 'balanceLab.coverage.reason.unknown';
  }
}

function tabIndex(key: string, current: number, count: number) {
  switch (key) {
    case 'ArrowLeft':
      return (current - 1 + count) % count;
    case 'ArrowRight':
      return (current + 1) % count;
    case 'Home':
      return 0;
    case 'End':
      return count - 1;
    default:
      return null;
  }
}
