/* SPDX-License-Identifier: GPL-3.0-only */

import { formatCompactMemory, setHeaderMemoryEnabled, useHeaderMemoryEnabled, useProcessMemory } from './processMemory';
import { useLocalization } from '../../localization';
import './ProcessMemoryPanel.css';

export function HeaderMemoryUsage() {
  const enabled = useHeaderMemoryEnabled();
  const { snapshot, status } = useProcessMemory(enabled);
  const { t, formatLocale } = useLocalization();
  if (!enabled) return null;
  const value = status === 'ready' && snapshot
    ? formatCompactMemory(snapshot.total.privateRamBytes, formatLocale, t('settings.memory.unavailableValue'))
    : status === 'loading' ? '…' : t('settings.memory.unavailableValue');
  return <span className="km-header-memory" title={t('settings.memory.headerHelp')}>
    {t('settings.memory.headerValue', { value })}
  </span>;
}

export function ProcessMemoryPanel() {
  const { t, formatLocale } = useLocalization();
  const { snapshot, status } = useProcessMemory();
  const headerEnabled = useHeaderMemoryEnabled();
  const formatMemory = (bytes: number | null) => bytes === null
    ? t('settings.memory.unavailableValue')
    : `${(bytes / (1024 * 1024)).toLocaleString(formatLocale, { maximumFractionDigits: 1 })} MiB`;
  const groups = ['desktop', 'workers', 'webView'] as const;
  return (
    <section aria-labelledby="process-memory-heading" className="km-settings-group">
      <h3 id="process-memory-heading">{t('settings.memory.title')}</h3>
      <div className="km-settings-group-body">
        <label className="checkbox-field">
          <input type="checkbox" className="km-choice-control" checked={headerEnabled}
            onChange={event => setHeaderMemoryEnabled(event.target.checked)} />
          <span>{t('settings.memory.showHeader')}</span>
        </label>
        <p className="field-note">{t('settings.memory.headerHelp')}</p>
        <p>{t('settings.memory.description')}</p>
        {status === 'ready' && snapshot ? (
          <>
            <dl className="km-memory-totals">
              <div><dt>{t('settings.memory.ram')}</dt><dd>{formatMemory(snapshot.total.privateRamBytes)}</dd></div>
              <div><dt>{t('settings.memory.committed')}</dt><dd>{formatMemory(snapshot.total.committedBytes)}</dd></div>
            </dl>
            <dl className="km-memory-totals">
              <div><dt>{t('settings.memory.systemTotal')}</dt><dd>{formatMemory(snapshot.system?.totalBytes ?? null)}</dd></div>
              <div><dt>{t('settings.memory.systemAvailable')}</dt><dd>{formatMemory(snapshot.system?.availableBytes ?? null)}</dd></div>
            </dl>
            <p className="km-settings-note">{t('settings.memory.adaptiveHelp')}</p>
            {snapshot.idleWorkerRetentionSeconds != null && <p className="km-settings-note">
              {t('settings.memory.retention', { seconds: snapshot.idleWorkerRetentionSeconds.toLocaleString(formatLocale) })}
            </p>}
            <p className="km-settings-note">{t('settings.memory.metricHelp')}</p>
            {snapshot.total.unreadableCount > 0 ? (
              <p role="status">{t('settings.memory.incomplete', { count: snapshot.total.unreadableCount })}</p>
            ) : snapshot.total.privateRamBytes === null ? (
              <p role="status">{t('settings.memory.ramUnavailable')}</p>
            ) : null}
            <div className="km-memory-table-wrap" role="region" aria-label={t('settings.memory.breakdown')} tabIndex={0}>
              <table>
                <thead><tr>
                  <th scope="col">{t('settings.memory.component')}</th>
                  <th scope="col">{t('settings.memory.processes')}</th>
                  <th scope="col">{t('settings.memory.ram')}</th>
                  <th scope="col">{t('settings.memory.committed')}</th>
                </tr></thead>
                <tbody>{groups.map(group => <tr key={group}>
                  <th scope="row">{t(`settings.memory.${group}`)}</th>
                  <td>{snapshot[group].processCount.toLocaleString(formatLocale)}</td>
                  <td>{formatMemory(snapshot[group].privateRamBytes)}</td>
                  <td>{formatMemory(snapshot[group].committedBytes)}</td>
                </tr>)}</tbody>
              </table>
            </div>
          </>
        ) : <p role="status">{t(`settings.memory.${status}`)}</p>}
      </div>
    </section>
  );
}
