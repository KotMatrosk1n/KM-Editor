/* SPDX-License-Identifier: GPL-3.0-only */

import { ExternalLink, ListTree } from 'lucide-react';
import { useId } from 'react';
import type {
  GameModuleConfidence,
  GameModuleFact,
  GameModuleRecord,
  QueryGameModuleResponse
} from '../../bridge/gameModuleContracts';
import { containsGameModuleLocalPathSignature } from '../../bridge/gameModuleContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';
import type { ApiDiagnostic } from '../../bridge/contracts';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { ReportableDiagnosticIssuesLink } from '../../components/ReportableErrorScreen';
import { useDiagnosticNavigation } from '../../diagnosticActions';
import { formatDiagnosticSummary } from '../../diagnostics';
import { useLocalization } from '../../localization';
import {
  presentFactValue,
  presentationFactLabelKey,
  relativeRecordTitle,
  humanizeIdentifier,
  diagnosticTechnicalIdentity,
  diagnosticSeverityPriority,
  groupDiagnosticsForPresentation,
  presentationDiagnosticMessage,
  presentationDiagnosticSeverity
} from '../workbench/analysisPresentationUtils';
import {
  DiagnosticSeverityText,
  DiagnosticTechnicalDetails,
  OccurrenceCount,
  TechnicalDetails
} from '../workbench/AnalysisPresentation';
import {
  presentGameModuleFactLabel,
  presentGameModuleFactValue,
  presentGameModuleRecordSummary,
  presentGameModuleRecordTitle
} from './gameModulePresentation';

export function GameModuleResults({
  canNavigateRecord,
  onNavigateRecord,
  preserveRecordOrder = false,
  showResultCount = true,
  response
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  preserveRecordOrder?: boolean;
  response: QueryGameModuleResponse;
  showResultCount?: boolean;
}) {
  const { t } = useLocalization();
  const groups = preserveRecordOrder
    ? response.records.map((root) => ({
        hierarchyBoundary: null,
        parentRecordId: root.parentRecordId,
        related: [],
        root
      }))
    : groupRecords(response.records);
  return (
    <div className="km-game-module-results">
      {showResultCount ? (
        <p aria-live="polite" className="km-game-module-result-count">
          {t('gameModules.results.count', {
            loaded: response.records.length,
            total: response.totalRecordCount
          })}
        </p>
      ) : null}
      {response.records.length > 0 ? (
        <ol aria-label={t('gameModules.results.label')}>
          {groups.map(({ hierarchyBoundary, parentRecordId, related, root }) => (
            <GameModuleResultCard
              canNavigateRecord={canNavigateRecord}
              hierarchyBoundary={hierarchyBoundary}
              key={root.recordId}
              onNavigateRecord={onNavigateRecord}
              parentRecordId={parentRecordId}
              related={related}
              root={root}
            />
          ))}
        </ol>
      ) : (
        <p className="km-workbench-empty">{t('gameModules.results.empty')}</p>
      )}
    </div>
  );
}

function GameModuleResultCard({
  canNavigateRecord,
  hierarchyBoundary,
  onNavigateRecord,
  parentRecordId,
  related,
  root
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  hierarchyBoundary: 'cycle' | 'missingParent' | null;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  parentRecordId: string | null;
  related: readonly GameModuleRecord[];
  root: GameModuleRecord;
}) {
  const { t, translateLiteral } = useLocalization();
  const rootTitle = presentGameModuleRecordTitle(root, t) ?? root.title;
  const rootSummary = presentGameModuleRecordSummary(root, t) ?? root.summary;
  return (
    <li>
      <article>
        <header>
          <ListTree aria-hidden="true" size={17} />
          <div data-localization-ignore="true">
            <h3>{rootTitle}</h3>
            {rootSummary ? <p>{rootSummary}</p> : null}
          </div>
          <div className="km-game-module-record-badges">
            <span data-state={root.coverage}>
              {t(`gameModules.state.${root.coverage}`)}
            </span>
            <ConfidenceBadge confidence={root.confidence} />
          </div>
        </header>
        {hierarchyBoundary ? (
          <p className="km-game-module-record-boundary">
            {t(hierarchyBoundary === 'cycle'
              ? 'gameModules.results.parentCycle'
              : 'gameModules.results.parentNotLoaded')}
          </p>
        ) : null}
        <RecordFacts facts={root.facts} recordConfidence={root.confidence} />
        <RecordNavigation
          canNavigateRecord={canNavigateRecord}
          onNavigateRecord={onNavigateRecord}
          record={root}
        />
        {related.length > 0 ? (
          <div className="km-analysis-related-records">
            {related.map((record) => (
              <div className="km-analysis-related-record" key={record.recordId}>
                <header>
                  <div data-localization-ignore="true">
                    <strong>
                      {presentGameModuleRecordTitle(record, t) ?? relatedRecordTitle(record, root)}
                    </strong>
                    {(presentGameModuleRecordSummary(record, t) ?? record.summary) &&
                    (presentGameModuleRecordSummary(record, t) ?? record.summary) !== rootSummary ? (
                      <p>{presentGameModuleRecordSummary(record, t) ?? record.summary}</p>
                    ) : null}
                  </div>
                  <div>
                    {record.coverage !== root.coverage ? (
                      <span data-state={record.coverage}>
                        {t(`gameModules.state.${record.coverage}`)}
                      </span>
                    ) : null}
                    <ConfidenceBadge confidence={record.confidence} />
                  </div>
                </header>
                <RecordFacts facts={record.facts} recordConfidence={record.confidence} />
                <RecordNavigation
                  canNavigateRecord={canNavigateRecord}
                  onNavigateRecord={onNavigateRecord}
                  record={record}
                />
                <TechnicalDetails summary={translateLiteral('Technical details')}>
                  <code>{record.title}</code>
                  <code>{record.recordKind}</code>
                  <code>{record.recordId}</code>
                </TechnicalDetails>
              </div>
            ))}
          </div>
        ) : null}
        <TechnicalDetails summary={translateLiteral('Technical details')}>
          <code>{root.recordKind}</code>
          <code>{root.recordId}</code>
          {parentRecordId ? <code>{parentRecordId}</code> : null}
          {root.groupId ? <code>{root.groupId}</code> : null}
        </TechnicalDetails>
      </article>
    </li>
  );
}

function RecordFacts({
  facts,
  recordConfidence
}: {
  facts: readonly GameModuleFact[];
  recordConfidence: GameModuleConfidence;
}) {
  const { t, translateLiteral } = useLocalization();
  const entries = gameFactEntries(facts, (fact) => {
    const localizedLabel = presentGameModuleFactLabel(fact, t);
    const labelKey = presentationFactLabelKey(fact.label);
    return localizedLabel ?? (labelKey ? t(labelKey) : fact.label);
  });
  const groups = (['verified', 'derived', 'unknown'] as const)
    .map((confidence) => ({
      confidence,
      facts: entries.filter(({ fact }) => fact.confidence === confidence)
    }))
    .filter((group) => group.facts.length > 0);
  if (groups.length === 0) return null;
  return (
    <div>
      {groups.map((group) => {
        const showTrustHeading = groups.length > 1 ||
          group.confidence !== recordConfidence ||
          group.confidence !== 'verified';
        return (
          <div className="km-analysis-confidence-group" key={group.confidence}>
            {showTrustHeading ? (
              <div className="km-analysis-confidence-heading">
                <ConfidenceBadge confidence={group.confidence} />
                <small>{group.facts.length.toLocaleString()}</small>
              </div>
            ) : null}
            <dl>
              {group.facts.map(({ fact, key, label }) => {
                const localizedFactValue = presentGameModuleFactValue(fact, t);
                const value = presentFactValue(
                  fact.label,
                  localizedFactValue ?? fact.value.displayValue,
                  fact.unit,
                  translateLiteral
                );
                return (
                  <div key={key}>
                    <dt data-localization-ignore="true">{label}</dt>
                    <dd data-localization-ignore="true">
                      <span>{value.displayValue}</span>
                      {value.unit ? <small>{value.unit}</small> : null}
                      {value.changed || localizedFactValue !== null ? (
                        <TechnicalDetails summary={translateLiteral('Technical details')}>
                          <code>
                            {fact.fieldKey}: {fact.value.displayValue}
                            {fact.unit ? ` ${fact.unit}` : ''}
                          </code>
                        </TechnicalDetails>
                      ) : null}
                    </dd>
                  </div>
                );
              })}
            </dl>
          </div>
        );
      })}
    </div>
  );
}

function RecordNavigation({
  canNavigateRecord,
  onNavigateRecord,
  record
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  record: GameModuleRecord;
}) {
  const { t } = useLocalization();
  if (!record.target) return null;
  return canNavigateRecord(record.target) ? (
    <button
      aria-label={`${t('gameModules.results.openRecord')}: ${record.title}, ${semanticRecordIdentity(record.target)}`}
      className="secondary-button compact-button km-game-module-record-open"
      onClick={() => onNavigateRecord(record.target!)}
      type="button"
    >
      <ExternalLink aria-hidden="true" size={14} />
      <span>{t('gameModules.results.openRecord')}</span>
    </button>
  ) : (
    <p className="km-game-module-record-unavailable">
      {t('gameModules.results.navigationUnavailable')}
    </p>
  );
}

function semanticRecordIdentity(record: SemanticExploreRecordRef) {
  return [
    record.gameFamily,
    record.domain,
    `${record.recordKind.key}@${record.recordKind.schemaVersion}`,
    record.recordId,
    record.subrecordId
  ].filter(Boolean).join(' / ');
}

function gameFactEntries(
  facts: readonly GameModuleFact[],
  labelFor: (fact: GameModuleFact) => string
) {
  const entries = facts.map((fact, index) => ({ fact, index, label: labelFor(fact) }));
  const withField = appendFactDiscriminator(entries, (entry) => entry.fact.fieldKey);
  const withProvider = appendFactDiscriminator(withField, (entry) => entry.fact.providerId);
  const withFact = appendFactDiscriminator(withProvider, (entry) => entry.fact.factId);
  const counts = countFactLabels(withFact.map((entry) => entry.label));
  const occurrences = new Map<string, number>();
  return withFact.map((entry) => {
    const normalized = entry.label.toLocaleLowerCase();
    const occurrence = (occurrences.get(normalized) ?? 0) + 1;
    occurrences.set(normalized, occurrence);
    return {
      fact: entry.fact,
      key: `${entry.fact.factId}:${entry.index}`,
      label: (counts.get(normalized) ?? 0) > 1
        ? `${entry.label} #${occurrence}`
        : entry.label
    };
  });
}

function appendFactDiscriminator<T extends { label: string }>(
  entries: readonly T[],
  discriminator: (entry: T) => string
) {
  const counts = countFactLabels(entries.map((entry) => entry.label));
  return entries.map((entry) => ({
    ...entry,
    label: (counts.get(entry.label.toLocaleLowerCase()) ?? 0) > 1
      ? `${entry.label} [${discriminator(entry)}]`
      : entry.label
  }));
}

function countFactLabels(labels: readonly string[]) {
  const counts = new Map<string, number>();
  for (const label of labels) {
    const normalized = label.toLocaleLowerCase();
    counts.set(normalized, (counts.get(normalized) ?? 0) + 1);
  }
  return counts;
}

export function ConfidenceBadge({ confidence }: { confidence: GameModuleConfidence }) {
  const { t } = useLocalization();
  return (
    <span className="km-game-module-confidence" data-confidence={confidence}>
      {t(`gameModules.confidence.${confidence}`)}
    </span>
  );
}

export function GameModuleDiagnostics({
  diagnostics
}: {
  diagnostics: readonly ApiDiagnostic[];
}) {
  const { t, translateLiteral } = useLocalization();
  const diagnosticNavigation = useDiagnosticNavigation();
  const headingId = useId();
  usePublishCommonEditorDiagnostics(diagnostics);
  if (diagnostics.length === 0) return null;
  const formatMessage = (diagnostic: ApiDiagnostic) => (
    safeDiagnosticMessage(diagnostic.message)
      ? formatDiagnosticSummary(diagnostic, translateLiteral, t)
      : t('gameModules.diagnostics.redacted')
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
    diagnosticTechnicalIdentity,
    (diagnostic) => diagnosticSeverityPriority(presentedSeverity(diagnostic))
  );
  const primaryAction = [...diagnostics]
    .sort((left, right) => (
      diagnosticSeverityPriority(right.severity) - diagnosticSeverityPriority(left.severity)
    ))
    .map((diagnostic) => diagnosticNavigation.resolveAction(diagnostic))
    .find((action) => action !== null);
  return (
    <section aria-labelledby={headingId} className="km-game-module-diagnostics">
      <div className="km-analysis-diagnostic-heading">
        <h3 id={headingId}>{t('gameModules.diagnostics.title')}</h3>
        {primaryAction ? (
          <button
            className="secondary-button compact-button"
            onClick={() => diagnosticNavigation.navigate(primaryAction.location)}
            type="button"
          >
            {t('diagnostics.openAction', {
              target: translateLiteral(primaryAction.targetLabel)
            })}
          </button>
        ) : null}
      </div>
      <ul>
        {grouped.slice(0, 50).map(({ count, diagnostics: identities, key }) => {
          const diagnostic = identities[0]!.diagnostic;
          return (
          <li data-severity={presentedSeverity(diagnostic)} key={key}>
            <span>
              <span>
                <DiagnosticSeverityText severity={presentedSeverity(diagnostic)} />
                {presentedMessage(diagnostic)}
              </span>
              <OccurrenceCount count={count} />
              <ReportableDiagnosticIssuesLink
                messages={identities.map((identity) => identity.diagnostic.message)}
              />
            </span>
            <DiagnosticTechnicalDetails
              diagnostics={identities}
              summary={translateLiteral('Technical details')}
            />
          </li>
          );
        })}
      </ul>
      {grouped.length > 50 ? <p>{t('gameModules.diagnostics.bounded')}</p> : null}
    </section>
  );
}

function safeDiagnosticMessage(message: string) {
  return !containsGameModuleLocalPathSignature(message);
}

function groupRecords(records: readonly GameModuleRecord[]) {
  const byId = new Map(records.map((record) => [record.recordId, record]));
  const children = new Map<string, GameModuleRecord[]>();
  for (const record of records) {
    if (!record.parentRecordId || !byId.has(record.parentRecordId)) continue;
    const siblings = children.get(record.parentRecordId);
    if (siblings) siblings.push(record);
    else children.set(record.parentRecordId, [record]);
  }
  const roots = records.filter((record) => (
    !record.parentRecordId || !byId.has(record.parentRecordId)
  ));
  const emitted = new Set<string>();
  const collect = (root: GameModuleRecord) => {
    const related: GameModuleRecord[] = [];
    const pending = [...(children.get(root.recordId) ?? [])].reverse();
    const visited = new Set([root.recordId]);
    while (pending.length > 0) {
      const record = pending.pop()!;
      if (visited.has(record.recordId) || emitted.has(record.recordId)) continue;
      visited.add(record.recordId);
      emitted.add(record.recordId);
      related.push(record);
      pending.push(...[...(children.get(record.recordId) ?? [])].reverse());
    }
    return related;
  };
  const groups: Array<{
    hierarchyBoundary: 'cycle' | 'missingParent' | null;
    parentRecordId: string | null;
    related: GameModuleRecord[];
    root: GameModuleRecord;
  }> = [];
  for (const root of roots) {
    if (emitted.has(root.recordId)) continue;
    emitted.add(root.recordId);
    groups.push({
      hierarchyBoundary: root.parentRecordId ? 'missingParent' : null,
      parentRecordId: root.parentRecordId,
      related: collect(root),
      root
    });
  }
  for (const record of records) {
    if (emitted.has(record.recordId)) continue;
    emitted.add(record.recordId);
    groups.push({
      hierarchyBoundary: 'cycle',
      parentRecordId: record.parentRecordId,
      related: collect(record),
      root: record
    });
  }
  return groups;
}

function relatedRecordTitle(record: GameModuleRecord, root: GameModuleRecord) {
  if (record.title !== root.title) return relativeRecordTitle(record.title, root.title);
  const battleStage = record.facts.find((fact) => fact.fieldKey === 'battleStage');
  const hpPhase = record.facts.find((fact) => fact.fieldKey === 'hpPhase');
  if (battleStage && hpPhase) {
    return `${battleStage.label} ${battleStage.value.displayValue} - ` +
      `${hpPhase.label} ${hpPhase.value.displayValue}`;
  }
  return humanizeIdentifier(record.recordKind);
}
