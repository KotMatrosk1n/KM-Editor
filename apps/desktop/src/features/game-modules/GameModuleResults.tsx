/* SPDX-License-Identifier: GPL-3.0-only */

import { ExternalLink, ListTree } from 'lucide-react';
import type {
  GameModuleConfidence,
  GameModuleFact,
  GameModuleRecord,
  QueryGameModuleResponse
} from '../../bridge/gameModuleContracts';
import { containsGameModuleLocalPathSignature } from '../../bridge/gameModuleContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';
import type { ApiDiagnostic } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import {
  presentFactValue,
  presentationFactLabelKey,
  relativeRecordTitle,
  humanizeIdentifier,
  groupDiagnosticsForPresentation
} from '../workbench/analysisPresentationUtils';
import {
  DiagnosticTechnicalDetails,
  OccurrenceCount,
  TechnicalDetails
} from '../workbench/AnalysisPresentation';

export function GameModuleResults({
  canNavigateRecord,
  onNavigateRecord,
  response
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  response: QueryGameModuleResponse;
}) {
  const { t } = useLocalization();
  const groups = groupRecords(response.records);
  return (
    <div className="km-game-module-results">
      <p aria-live="polite" className="km-game-module-result-count">
        {t('gameModules.results.count', {
          loaded: response.records.length,
          total: response.totalRecordCount
        })}
      </p>
      {response.records.length > 0 ? (
        <ol aria-label={t('gameModules.results.label')}>
          {groups.map(({ related, root }) => (
            <GameModuleResultCard
              canNavigateRecord={canNavigateRecord}
              key={root.recordId}
              onNavigateRecord={onNavigateRecord}
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
  onNavigateRecord,
  related,
  root
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  related: readonly GameModuleRecord[];
  root: GameModuleRecord;
}) {
  const { t, translateLiteral } = useLocalization();
  return (
    <li>
      <article>
        <header>
          <ListTree aria-hidden="true" size={17} />
          <div data-localization-ignore="true">
            <h3>{root.title}</h3>
            {root.summary ? <p>{root.summary}</p> : null}
          </div>
          <div className="km-game-module-record-badges">
            <span data-state={root.coverage}>
              {t(`gameModules.state.${root.coverage}`)}
            </span>
            <ConfidenceBadge confidence={root.confidence} />
          </div>
        </header>
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
                    <strong>{relatedRecordTitle(record, root)}</strong>
                    {record.summary && record.summary !== root.summary ? <p>{record.summary}</p> : null}
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
  const groups = (['verified', 'derived', 'unknown'] as const)
    .map((confidence) => ({
      confidence,
      facts: facts.filter((fact) => fact.confidence === confidence)
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
              {group.facts.map((fact) => {
                const value = presentFactValue(
                  fact.label,
                  fact.value.displayValue,
                  fact.unit,
                  translateLiteral
                );
                const labelKey = presentationFactLabelKey(fact.label);
                return (
                  <div key={fact.factId}>
                    <dt data-localization-ignore="true">{labelKey ? t(labelKey) : fact.label}</dt>
                    <dd data-localization-ignore="true">
                      <span>{value.displayValue}</span>
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
  if (diagnostics.length === 0) return null;
  const grouped = groupDiagnosticsForPresentation(
    diagnostics,
    (diagnostic) => [
      diagnostic.severity,
      safeDiagnosticMessage(diagnostic.message) ? diagnostic.message : 'redacted'
    ],
    (diagnostic) => [diagnostic.code, diagnostic.domain, diagnostic.field]
  );
  return (
    <section aria-labelledby="game-module-diagnostics-title" className="km-game-module-diagnostics">
      <h3 id="game-module-diagnostics-title">{t('gameModules.diagnostics.title')}</h3>
      <ul>
        {grouped.slice(0, 50).map(({ count, diagnostics: identities, key }) => {
          const diagnostic = identities[0]!.diagnostic;
          return (
          <li data-severity={diagnostic.severity} key={key}>
            <span>
              {safeDiagnosticMessage(diagnostic.message) ? (
                <span data-localization-ignore="true">{diagnostic.message}</span>
              ) : (
                <span>{t('gameModules.diagnostics.redacted')}</span>
              )}
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
  const collect = (recordId: string): GameModuleRecord[] => (
    (children.get(recordId) ?? []).flatMap((record) => [record, ...collect(record.recordId)])
  );
  return roots.map((root) => ({ related: collect(root.recordId), root }));
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
