/* SPDX-License-Identifier: GPL-3.0-only */

import { Activity, Check, CircleAlert, Clock3, Gauge } from 'lucide-react';
import { useId } from 'react';
import { useLocalization } from '../../localization';
import {
  analysisSystemIds,
  analysisPreloadOrder,
  type AnalysisLoadingMode,
  type AnalysisPreparationSnapshot,
  type AnalysisSystemId
} from './analysisPreparation';

export function AnalysisPreparationPanel({
  mode,
  snapshot
}: {
  mode: AnalysisLoadingMode;
  snapshot: AnalysisPreparationSnapshot;
}) {
  const { t } = useLocalization();
  const titleId = useId();
  const isReady = snapshot.readyCount === snapshot.targetCount && snapshot.errorCount === 0;
  const hasError = snapshot.errorCount > 0;
  const phase = hasError ? 'error' : isReady ? 'ready' : 'preparing';
  const targetedTools = new Set<AnalysisSystemId>([
    'semanticProject',
    ...analysisPreloadOrder(mode)
  ]);

  return (
    <section aria-labelledby={titleId} className="analysis-preparation-panel">
      <div className="analysis-preparation-header">
        <div>
          <Gauge aria-hidden="true" size={18} />
          <strong id={titleId}>{t('analysisPreparation.title')}</strong>
        </div>
        <span className={`status-pill status-pill-info${hasError ? ' is-error' : ''}`}>
          {t(`analysisPreparation.phase.${phase}`)}
        </span>
      </div>
      <div
        aria-label={t('analysisPreparation.progressLabel')}
        aria-valuemax={100}
        aria-valuemin={0}
        aria-valuenow={snapshot.percent}
        aria-valuetext={t('analysisPreparation.summary', {
          completed: snapshot.completedUnitCount,
          percent: snapshot.percent,
          total: snapshot.totalUnitCount
        })}
        className="work-progress-track"
        role="progressbar"
      >
        <div className="work-progress-fill" style={{ width: `${snapshot.percent}%` }} />
      </div>
      <p className="analysis-preparation-summary">
        {t('analysisPreparation.summary', {
          completed: snapshot.completedUnitCount,
          percent: snapshot.percent,
          total: snapshot.totalUnitCount
        })}
      </p>
      <ul className="analysis-preparation-systems">
        {analysisSystemIds.map((system) => {
          const isTargeted = targetedTools.has(system);
          const progress = snapshot.progressBySystem[system];
          const state = !isTargeted && snapshot.states[system] === 'waiting'
            ? 'onDemand'
            : snapshot.states[system];
          return (
            <li key={system}>
              {state === 'ready' ? (
                <Check aria-hidden="true" size={15} />
              ) : state === 'error' ? (
                <CircleAlert aria-hidden="true" size={15} />
              ) : state === 'loading' ? (
                <Activity aria-hidden="true" size={15} />
              ) : (
                <Clock3 aria-hidden="true" size={15} />
              )}
              <span>{t(`analysisPreparation.system.${system}`)}</span>
              <small>
                {t(`analysisPreparation.state.${state}`)}
                {isTargeted && state !== 'onDemand'
                  ? ` (${progress.completedUnitCount}/${progress.totalUnitCount})`
                  : null}
              </small>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
