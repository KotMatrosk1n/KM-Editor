/* SPDX-License-Identifier: GPL-3.0-only */

import { History, RefreshCw } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { DiagnosticsSection } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import type { OutputSafetyController } from './useOutputSafetyController';
import './OutputHistoryPage.css';

export function localHistoryDay(value: string) {
  const date = new Date(value);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function OutputHistoryPage({ controller }: { controller: OutputSafetyController }) {
  const { t, formatLocale, translateLiteral } = useLocalization();
  const { history, isAvailable, busyAction, loadHistory } = controller;
  const attemptedLoad = useRef(false);
  const [selectedDay, setSelectedDay] = useState<string | null>(null);
  useEffect(() => {
    if (!attemptedLoad.current && isAvailable && busyAction === null) {
      attemptedLoad.current = true;
      void loadHistory();
    }
  }, [isAvailable, busyAction, loadHistory]);
  const days = useMemo(() => {
    const result = new Map<string, NonNullable<typeof history>['receipts']>();
    for (const receipt of [...(history?.receipts ?? [])].sort((left, right) =>
      right.completedAtUtc.localeCompare(left.completedAtUtc) || right.transactionId.localeCompare(left.transactionId))) {
      const day = localHistoryDay(receipt.completedAtUtc);
      result.set(day, [...(result.get(day) ?? []), receipt]);
    }
    return result;
  }, [history]);
  const activeDay = selectedDay && days.has(selectedDay) ? selectedDay : days.keys().next().value;
  const dayFormat = new Intl.DateTimeFormat(formatLocale, { dateStyle: 'full' });
  const timeFormat = new Intl.DateTimeFormat(formatLocale, { timeStyle: 'short' });
  return <section className="panel wide-panel output-history-page" aria-labelledby="output-history-page-heading">
    <div className="output-history-page-heading">
      <div>
        <div className="panel-heading"><History aria-hidden="true" size={20} />
          <h2 id="output-history-page-heading">{t('history.title')}</h2>
        </div>
        <p className="output-history-description">{t('history.description')}</p>
      </div>
      <button className="secondary-button" disabled={!isAvailable || busyAction !== null}
        onClick={() => void loadHistory()} type="button"><RefreshCw aria-hidden="true" size={16} />{t('outputSafety.refresh')}</button>
    </div>
    {!isAvailable ? <p>{t('history.unavailable')}</p> : !history ? <p role="status">{t(busyAction ? 'history.loading' : 'history.loadFailed')}</p> : days.size === 0 ?
      <p>{t('outputSafety.history.empty')}</p> : <div className="output-history-browser">
      <nav className="output-history-days" aria-label={t('history.days')}>
        {[...days].map(([day, receipts]) => <button type="button" key={day}
          className={`secondary-button${day === activeDay ? ' is-selected' : ''}`} aria-current={day === activeDay ? 'date' : undefined}
          onClick={() => setSelectedDay(day)}>
          <span>{dayFormat.format(new Date(receipts[0]!.completedAtUtc))}</span>
          <small>{t('history.transactions', { count: receipts.length })}</small>
        </button>)}
        {history.nextCursor ? <button type="button" className="secondary-button" disabled={busyAction !== null}
          onClick={() => void controller.loadMoreHistory()}>{t('outputSafety.history.loadMore')}</button> : null}
      </nav>
      <div className="output-history-transactions" aria-live="polite">
        {(activeDay ? days.get(activeDay) : [])?.map(receipt => <article key={receipt.transactionId} className={`output-history-transaction output-transaction-${receipt.outcome}`}>
          <h3><time dateTime={receipt.completedAtUtc}>{timeFormat.format(new Date(receipt.completedAtUtc))}</time>{' - '}
            {t(`outputSafety.transaction.outcome.${receipt.outcome}`)}</h3>
          <p>{t('history.outputSummary', { count: receipt.targetCount, mode: receipt.outputMode })}</p>
          {receipt.historyDetails ? <>
            <p>{t('history.changeCount', { count: receipt.historyDetails.totalChangeCount })}</p>
            <ol className="output-history-changes">{receipt.historyDetails.changes.map((change, index) => <li key={index}>
              <strong>{translateLiteral(change.summary)}</strong>
              <dl><div><dt>{t('history.record')}</dt><dd data-localization-ignore="true">{change.recordId ?? change.domain}</dd></div>
                {change.field ? <div><dt>{t('history.field')}</dt><dd data-localization-ignore="true">{change.field}</dd></div> : null}
                {change.newValue !== null ? <div><dt>{t('history.value')}</dt><dd data-localization-ignore="true">{change.newValue}</dd></div> : null}</dl>
            </li>)}</ol>
            {receipt.historyDetails.truncated ? <p>{t('history.limitedDetails')}</p> : null}
          </> : <p>{t('history.olderReceipt')}</p>}
          <details><summary>{t('history.files')}</summary><ul>{receipt.targets?.map(target =>
            <li key={target.relativePath}>{receipt.outcome === 'committed' ? <span>{t(target.kind === 'Delete' ? 'history.deleted' : 'history.written')}{' '}</span> : null}
              <code data-localization-ignore="true">{target.relativePath}</code></li>)}</ul></details>
          <details><summary>{t('workflowPanels.outputTransaction.details')}</summary>
            <dl><div><dt>{t('workflowPanels.outputTransaction.id')}</dt><dd data-localization-ignore="true">{receipt.transactionId}</dd></div></dl>
            {receipt.outcomeCode ? <code data-localization-ignore="true">{receipt.outcomeCode}</code> : null}
          </details>
        </article>)}
      </div>
    </div>}
    <DiagnosticsSection diagnostics={controller.actionDiagnostics} />
  </section>;
}
