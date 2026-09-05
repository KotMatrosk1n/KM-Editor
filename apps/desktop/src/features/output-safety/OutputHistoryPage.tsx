/* SPDX-License-Identifier: GPL-3.0-only */

import { History, RefreshCw } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { OutputHistoryReceipt } from '../../bridge/outputSafetyContracts';
import { DiagnosticsSection } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import { getWorkbenchSectionLabelKey, workbenchCapabilityRegistry } from '../../workbench/capabilityRegistry';
import type { OutputSafetyController } from './useOutputSafetyController';
import './OutputHistoryPage.css';

const transactionsPerPage = 10;
const changesPerPage = 25;
const filesOnly = 'history:files-only';
type HistoryChange = NonNullable<OutputHistoryReceipt['historyDetails']>['changes'][number];

function HistoryPagination({ page, lastPage, onChange }: { page: number; lastPage: number; onChange: (page: number) => void }) {
  const { t } = useLocalization();
  return <div className="output-history-pagination">
    <button className="secondary-button" type="button" disabled={page === 0} onClick={() => onChange(page - 1)}>{t('history.previous')}</button>
    <span role="status">{t('history.page', { page: page + 1, total: lastPage + 1 })}</span>
    <button className="secondary-button" type="button" disabled={page === lastPage} onClick={() => onChange(page + 1)}>{t('history.next')}</button>
  </div>;
}

function HistoryChangeGroup({ changes, label }: { changes: HistoryChange[]; label: string }) {
  const { t, translateLiteral } = useLocalization();
  const [page, setPage] = useState(0);
  const lastPage = Math.max(0, Math.ceil(changes.length / changesPerPage) - 1);
  const activePage = Math.min(page, lastPage);
  return <section className="output-history-editor-group" aria-label={label}>
    <h4>{label} <small>{t('history.changeCount', { count: changes.length })}</small></h4>
    <ol className="output-history-changes" start={activePage * changesPerPage + 1}>
      {changes.slice(activePage * changesPerPage, (activePage + 1) * changesPerPage).map((change, index) => <li key={activePage * changesPerPage + index}>
        <strong>{translateLiteral(change.summary)}</strong>
        <details><summary>{t('advancedEditor.technicalDetails')}</summary>
          <dl><div><dt>{t('history.record')}</dt><dd data-localization-ignore="true">{change.recordId ?? change.domain}</dd></div>
            {change.field ? <div><dt>{t('history.field')}</dt><dd data-localization-ignore="true">{change.field}</dd></div> : null}
            {change.newValue !== null ? <div><dt>{t('history.value')}</dt><dd data-localization-ignore="true">{change.newValue}</dd></div> : null}</dl>
        </details>
      </li>)}
    </ol>
    {lastPage > 0 ? <HistoryPagination page={activePage} lastPage={lastPage} onChange={setPage} /> : null}
  </section>;
}

export function localHistoryDay(value: string) {
  const date = new Date(value);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function OutputHistoryPage({ controller }: { controller: OutputSafetyController }) {
  const { t, formatLocale, translateLiteral } = useLocalization();
  const { history, isAvailable, busyAction, loadHistory } = controller;
  const attemptedLoad = useRef(false);
  const [selectedDay, setSelectedDay] = useState<string | null>(null);
  const [selectedEditor, setSelectedEditor] = useState<string | null>(null);
  const [transactionPage, setTransactionPage] = useState(0);
  const scrollRef = useRef<HTMLDivElement>(null);
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
      const receipts = result.get(day) ?? [];
      receipts.push(receipt);
      result.set(day, receipts);
    }
    return result;
  }, [history]);
  const activeDay = selectedDay && days.has(selectedDay) ? selectedDay : days.keys().next().value;
  const dayReceipts = activeDay ? days.get(activeDay)! : [];
  const editors = new Map<string, number>();
  for (const receipt of dayReceipts) {
    const changes = receipt.historyDetails?.changes ?? [];
    if (!changes.length) editors.set(filesOnly, (editors.get(filesOnly) ?? 0) + 1);
    for (const change of changes) editors.set(change.domain, (editors.get(change.domain) ?? 0) + 1);
  }
  const activeEditor = selectedEditor && editors.has(selectedEditor) ? selectedEditor : editors.keys().next().value ?? null;
  const editorLabel = (domain: string) => {
    if (domain === filesOnly) return t('history.filesOnly');
    const registration = workbenchCapabilityRegistry.find(entry => entry.domain === domain || `workflow.${entry.id}` === domain);
    return registration ? t(getWorkbenchSectionLabelKey(registration.id)) : translateLiteral(domain);
  };
  const filteredReceipts = dayReceipts.filter(receipt => activeEditor === null || (activeEditor === filesOnly
    ? !receipt.historyDetails?.changes.length
    : receipt.historyDetails?.changes.some(change => change.domain === activeEditor)));
  const lastPage = Math.max(0, Math.ceil(filteredReceipts.length / transactionsPerPage) - 1);
  const activePage = Math.min(transactionPage, lastPage);
  useEffect(() => { if (scrollRef.current) scrollRef.current.scrollTop = 0; }, [activeDay, activeEditor, activePage]);
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
          onClick={() => { setSelectedDay(day); setSelectedEditor(null); setTransactionPage(0); }}>
          <span>{dayFormat.format(new Date(receipts[0]!.completedAtUtc))}</span>
          <small>{t('history.transactions', { count: receipts.length })}</small>
        </button>)}
        {history.nextCursor ? <button type="button" className="secondary-button" disabled={busyAction !== null}
          onClick={() => void controller.loadMoreHistory()}>{t('outputSafety.history.loadMore')}</button> : null}
      </nav>
      <div className="output-history-day-content">
        <div className="output-history-editors" role="group" aria-label={t('history.editors')}>
          {[...editors].map(([domain, count]) => <button type="button" key={domain} className="secondary-button" aria-pressed={activeEditor === domain}
            onClick={() => { setSelectedEditor(domain); setTransactionPage(0); }}>
            {editorLabel(domain)} <small>{t(domain === filesOnly ? 'history.transactions' : 'history.changeCount', { count })}</small>
          </button>)}
        </div>
      <div className="output-history-transactions" ref={scrollRef} tabIndex={0} role="region" aria-label={t('history.transactionsRegion')}>
        {filteredReceipts.slice(activePage * transactionsPerPage, (activePage + 1) * transactionsPerPage).map(receipt => {
          const groups = new Map<string, HistoryChange[]>();
          for (const change of receipt.historyDetails?.changes ?? []) {
            if (activeEditor !== null && activeEditor !== change.domain) continue;
            const changes = groups.get(change.domain) ?? [];
            changes.push(change); groups.set(change.domain, changes);
          }
          return <article key={`${activeDay}:${activeEditor}:${receipt.transactionId}`} className={`output-history-transaction output-transaction-${receipt.outcome}`}>
          <h3><time dateTime={receipt.completedAtUtc}>{timeFormat.format(new Date(receipt.completedAtUtc))}</time>{' - '}
            {t(`outputSafety.transaction.outcome.${receipt.outcome}`)}</h3>
          <p>{t('history.outputSummary', { count: receipt.targetCount, mode: receipt.outputMode })}</p>
          {receipt.historyDetails ? <>
            <p>{t('history.changeCount', { count: receipt.historyDetails.totalChangeCount })}</p>
            {[...groups].map(([domain, changes]) => <HistoryChangeGroup key={domain} changes={changes} label={editorLabel(domain)} />)}
            {receipt.historyDetails.truncated ? <p>{t('history.limitedDetails')}</p> : null}
          </> : <p>{t('history.olderReceipt')}</p>}
          <details className="output-history-file-details"><summary>{t('history.files')}</summary><ul>{receipt.targets?.map(target =>
            <li key={target.relativePath}>{receipt.outcome === 'committed' ? <span>{t(target.kind === 'Delete' ? 'history.deleted' : 'history.written')}{' '}</span> : null}
              <code data-localization-ignore="true">{target.relativePath}</code></li>)}</ul></details>
          <details><summary>{t('workflowPanels.outputTransaction.details')}</summary>
            <dl><div><dt>{t('workflowPanels.outputTransaction.id')}</dt><dd data-localization-ignore="true">{receipt.transactionId}</dd></div></dl>
            {receipt.outcomeCode ? <code data-localization-ignore="true">{receipt.outcomeCode}</code> : null}
          </details>
        </article>; })}
      </div>
      {lastPage > 0 ? <HistoryPagination page={activePage} lastPage={lastPage} onChange={setTransactionPage} /> : null}
      </div>
    </div>}
    <DiagnosticsSection diagnostics={controller.actionDiagnostics} />
  </section>;
}
