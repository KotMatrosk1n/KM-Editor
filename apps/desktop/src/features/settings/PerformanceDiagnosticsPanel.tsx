/* SPDX-License-Identifier: GPL-3.0-only */

import { useState, useSyncExternalStore } from 'react';
import { useLocalization } from '../../localization';
import {
  clearPerformanceDiagnostics,
  createPerformanceDiagnosticsSummary,
  getPerformanceDiagnosticsSnapshot,
  setPerformanceDiagnosticsEnabled,
  subscribeToPerformanceDiagnostics
} from '../../performanceDiagnostics';

export function PerformanceDiagnosticsPanel() {
  const { t } = useLocalization();
  const snapshot = useSyncExternalStore(
    subscribeToPerformanceDiagnostics,
    getPerformanceDiagnosticsSnapshot,
    getPerformanceDiagnosticsSnapshot
  );
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');

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
