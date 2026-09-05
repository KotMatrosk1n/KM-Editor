/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke, isTauri } from '@tauri-apps/api/core';
import { useEffect, useState } from 'react';
import { useLocalization } from '../../localization';
import './ProcessMemoryPanel.css';

type MemoryGroup = {
  processCount: number;
  unreadableCount: number;
  privateRamBytes: number | null;
  committedBytes: number | null;
};
type MemorySnapshot = Record<'desktop' | 'workers' | 'webView' | 'total', MemoryGroup>;

export function ProcessMemoryPanel() {
  const { t, formatLocale } = useLocalization();
  const [snapshot, setSnapshot] = useState<MemorySnapshot | null>(null);
  const [status, setStatus] = useState<'loading' | 'ready' | 'unavailable' | 'unsupported'>('loading');

  useEffect(() => {
    if (!isTauri()) { setStatus('unsupported'); return; }
    let stopped = false;
    let inFlight = false;
    let supported = true;
    const visible = () => document.visibilityState !== 'hidden';
    let timer: ReturnType<typeof setTimeout> | undefined;
    const sample = async () => {
      if (stopped || inFlight || !supported || !visible()) return;
      inFlight = true;
      try {
        const next = await invoke<MemorySnapshot>('get_app_memory');
        if (!stopped && visible()) {
          setSnapshot(next);
          setStatus('ready');
        }
      } catch (error) {
        if (error === 'unsupported') supported = false;
        if (!stopped) {
          setSnapshot(null);
          setStatus(error === 'unsupported' ? 'unsupported' : 'unavailable');
        }
      } finally {
        inFlight = false;
        if (!stopped && supported && visible()) timer = setTimeout(() => void sample(), 5_000);
      }
    };
    const visibilityChanged = () => {
      clearTimeout(timer);
      if (visible()) void sample();
    };
    document.addEventListener('visibilitychange', visibilityChanged);
    void sample();
    return () => {
      stopped = true;
      clearTimeout(timer);
      document.removeEventListener('visibilitychange', visibilityChanged);
    };
  }, []);

  const formatMemory = (bytes: number | null) => bytes === null
    ? t('settings.memory.unavailableValue')
    : `${(bytes / (1024 * 1024)).toLocaleString(formatLocale, { maximumFractionDigits: 1 })} MiB`;
  const groups = ['desktop', 'workers', 'webView'] as const;
  return (
    <section aria-labelledby="process-memory-heading" className="km-settings-group">
      <h3 id="process-memory-heading">{t('settings.memory.title')}</h3>
      <div className="km-settings-group-body">
        <p>{t('settings.memory.description')}</p>
        {status === 'ready' && snapshot ? (
          <>
            <dl className="km-memory-totals">
              <div><dt>{t('settings.memory.ram')}</dt><dd>{formatMemory(snapshot.total.privateRamBytes)}</dd></div>
              <div><dt>{t('settings.memory.committed')}</dt><dd>{formatMemory(snapshot.total.committedBytes)}</dd></div>
            </dl>
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
