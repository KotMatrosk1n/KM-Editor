/* SPDX-License-Identifier: GPL-3.0-only */

import { ExternalLink, ListTree } from 'lucide-react';
import type {
  GameModuleConfidence,
  GameModuleRecord,
  QueryGameModuleResponse
} from '../../bridge/gameModuleContracts';
import { containsGameModuleLocalPathSignature } from '../../bridge/gameModuleContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';

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
          {response.records.map((record) => (
            <GameModuleResultCard
              canNavigate={Boolean(record.target && canNavigateRecord(record.target))}
              key={record.recordId}
              onNavigate={() => {
                if (record.target) onNavigateRecord(record.target);
              }}
              record={record}
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
  canNavigate,
  onNavigate,
  record
}: {
  canNavigate: boolean;
  onNavigate: () => void;
  record: GameModuleRecord;
}) {
  const { t } = useLocalization();
  return (
    <li>
      <article>
        <header>
          <ListTree aria-hidden="true" size={17} />
          <div data-localization-ignore="true">
            <h3>{record.title}</h3>
            {record.summary ? <p>{record.summary}</p> : null}
          </div>
          <div className="km-game-module-record-badges">
            <span data-state={record.coverage}>
              {t(`gameModules.state.${record.coverage}`)}
            </span>
            <ConfidenceBadge confidence={record.confidence} />
          </div>
        </header>
        {record.facts.length > 0 ? (
          <dl>
            {record.facts.map((fact) => (
              <div key={fact.factId}>
                <dt data-localization-ignore="true">{fact.label}</dt>
                <dd data-localization-ignore="true">
                  <span>{fact.value.displayValue}</span>
                  {fact.unit ? <small>{fact.unit}</small> : null}
                  <ConfidenceBadge confidence={fact.confidence} />
                </dd>
              </div>
            ))}
          </dl>
        ) : null}
        {record.target ? (
          canNavigate ? (
            <button
              className="secondary-button compact-button km-game-module-record-open"
              onClick={onNavigate}
              type="button"
            >
              <ExternalLink aria-hidden="true" size={14} />
              <span>{t('gameModules.results.openRecord')}</span>
            </button>
          ) : (
            <p className="km-game-module-record-unavailable">
              {t('gameModules.results.navigationUnavailable')}
            </p>
          )
        ) : null}
      </article>
    </li>
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
  diagnostics: readonly { message: string; severity: string }[];
}) {
  const { t } = useLocalization();
  if (diagnostics.length === 0) return null;
  return (
    <section aria-labelledby="game-module-diagnostics-title" className="km-game-module-diagnostics">
      <h3 id="game-module-diagnostics-title">{t('gameModules.diagnostics.title')}</h3>
      <ul>
        {diagnostics.slice(0, 50).map((diagnostic, index) => (
          <li data-severity={diagnostic.severity} key={`${diagnostic.severity}:${index}`}>
            {safeDiagnosticMessage(diagnostic.message) ? (
              <span data-localization-ignore="true">{diagnostic.message}</span>
            ) : (
              <span>{t('gameModules.diagnostics.redacted')}</span>
            )}
          </li>
        ))}
      </ul>
      {diagnostics.length > 50 ? <p>{t('gameModules.diagnostics.bounded')}</p> : null}
    </section>
  );
}

function safeDiagnosticMessage(message: string) {
  return !containsGameModuleLocalPathSignature(message);
}
