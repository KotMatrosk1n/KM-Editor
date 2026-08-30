/* SPDX-License-Identifier: GPL-3.0-only */

import { useState, useSyncExternalStore } from 'react';
import { useLocalization } from '../../localization';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import {
  clearPerformanceDiagnostics,
  createPerformanceDiagnosticsSummary,
  formatPerformanceDiagnosticCommand,
  getPerformanceDiagnosticsSnapshot,
  setPerformanceDiagnosticsEnabled,
  summarizePerformanceDiagnostics,
  subscribeToPerformanceDiagnostics
} from '../../performanceDiagnostics';

export function PerformanceDiagnosticsPanel() {
  const { formatLocale, t } = useLocalization();
  const snapshot = useSyncExternalStore(
    subscribeToPerformanceDiagnostics,
    getPerformanceDiagnosticsSnapshot,
    getPerformanceDiagnosticsSnapshot
  );
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');
  usePublishCommonEditorError({
    domain: 'settings.performance',
    field: 'clipboard',
    message: copyState === 'failed' ? t('settings.performance.copyFailed') : null
  });
  const commandSummaries = summarizePerformanceDiagnostics(snapshot.samples);
  const formatDuration = (durationMs: number) =>
    `${durationMs.toLocaleString(formatLocale)} ms`;

  const copySummary = async () => {
    try {
      await navigator.clipboard.writeText(createPerformanceDiagnosticsSummary());
      setCopyState('copied');
    } catch {
      setCopyState('failed');
    }
  };

  return (
    <details aria-labelledby="performance-diagnostics-heading" className="km-settings-group">
      <summary>
        <h3 id="performance-diagnostics-heading">{t('settings.performance.title')}</h3>
      </summary>
      <div className="km-settings-group-body">
        <p>{t('settings.performance.description')}</p>
        <label className="km-settings-toggle">
          <input
            checked={snapshot.enabled}
            className="km-choice-control"
            onChange={(event) => setPerformanceDiagnosticsEnabled(event.currentTarget.checked)}
            type="checkbox"
          />
          <span>{t('settings.performance.enable')}</span>
        </label>
        <p className="km-settings-note">{t('settings.performance.privacy')}</p>
        <p aria-live="polite">
          {t('settings.performance.sampleCount', { count: snapshot.samples.length })}
        </p>
        <section
          aria-labelledby="performance-diagnostics-summary-heading"
          className="km-performance-summary"
        >
          <div className="km-performance-summary-heading">
            <h4 id="performance-diagnostics-summary-heading">
              {t('settings.performance.summary.title')}
            </h4>
            <p>{t('settings.performance.summary.description')}</p>
          </div>
          {commandSummaries.length === 0 ? (
            <p className="km-performance-summary-empty">
              {t('settings.performance.summary.empty')}
            </p>
          ) : (
            <div
              aria-label={t('settings.performance.summary.tableLabel')}
              className="km-performance-summary-table-wrap"
              role="region"
              tabIndex={0}
            >
              <table aria-label={t('settings.performance.summary.tableLabel')}>
                <thead>
                  <tr>
                    <th scope="col">{t('settings.performance.summary.command')}</th>
                    <th scope="col">{t('settings.performance.summary.samples')}</th>
                    <th scope="col">{t('settings.performance.summary.failures')}</th>
                    <th scope="col">{t('settings.performance.summary.median')}</th>
                    <th scope="col">{t('settings.performance.summary.p95')}</th>
                    <th scope="col">{t('settings.performance.summary.maximum')}</th>
                  </tr>
                </thead>
                <tbody>
                  {commandSummaries.map((summary) => (
                    <tr key={summary.command}>
                      <th scope="row">
                        <span data-localization-ignore="true">
                          {formatPerformanceDiagnosticCommand(summary.command)}
                        </span>
                        <code data-localization-ignore="true">{summary.command}</code>
                      </th>
                      <td>{summary.sampleCount.toLocaleString(formatLocale)}</td>
                      <td className={summary.failures > 0 ? 'km-performance-failures' : undefined}>
                        {summary.failures.toLocaleString(formatLocale)}
                      </td>
                      <td>{formatDuration(summary.medianDurationMs)}</td>
                      <td>{formatDuration(summary.p95DurationMs)}</td>
                      <td>{formatDuration(summary.maximumDurationMs)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
        <div className="km-settings-actions">
          <button
            disabled={snapshot.samples.length === 0}
            onClick={clearPerformanceDiagnostics}
            type="button"
          >
            {t('settings.performance.clear')}
          </button>
          <button
            disabled={snapshot.samples.length === 0}
            onClick={() => void copySummary()}
            type="button"
          >
            {t('settings.performance.copy')}
          </button>
        </div>
        <p aria-live="polite" className="km-settings-status">
          {copyState === 'copied'
            ? t('settings.performance.copied')
            : copyState === 'failed'
              ? t('settings.performance.copyFailed')
              : ''}
        </p>
      </div>
    </details>
  );
}
