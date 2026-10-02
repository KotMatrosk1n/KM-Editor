/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useState } from 'react';
import { CheckCircle2, RefreshCw, Save, Search, Wrench, X } from 'lucide-react';
import type { EditSession } from '../../bridge/contracts';
import type { FixAiFlagsSelection, FixAiFlagsWorkflow } from '../../bridge/fixAiFlagsContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './FixAiFlagsSection.css';

type Props = {
  workflow: FixAiFlagsWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (selections: FixAiFlagsSelection[], contextFingerprint: string, acknowledgeCustomScripts: boolean) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};
const hex = (value: number) => `0x${value.toString(16).toUpperCase().padStart(4, '0')}`;

export function FixAiFlagsSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral, formatLocale } = useLocalization();
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState('candidates');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [drafts, setDrafts] = useState<Record<number, FixAiFlagsSelection>>({});
  const [confirmed, setConfirmed] = useState(false);
  const [customContext, setCustomContext] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const trainers = workflow?.trainers ?? [];
  const pending = useMemo(() => new Map(session?.pendingEdits.filter(edit => edit.domain === 'workflow.trainers' && edit.field === 'aiFlags')
    .map(edit => [Number(edit.recordId), edit]) ?? []), [session]);
  const choices = Object.values(drafts);
  const dirty = choices.length > 0;
  const canEdit = isEditing && workflow?.canEdit === true;
  const eligible = (row: (typeof trainers)[number]) => row.candidate && !pending.has(row.trainerId);
  const stale = choices.some(choice => !trainers.some(row => row.trainerId === choice.trainerId && row.fingerprint === choice.fingerprint && eligible(row)));
  const visible = trainers.filter(row => (filter === 'all' || filter === 'reviewed' && drafts[row.trainerId] ||
    filter === 'fixed' && row.previouslyFixed || filter === 'candidates' && row.candidate)
    && `${row.trainerId} ${row.name}`.toLocaleLowerCase().includes(query.toLocaleLowerCase().trim()));
  const selected = trainers.find(row => row.trainerId === selectedId) ?? visible[0];
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.trainers', field: 'aiFlags', message: failed ? t('fixAiFlags.failed') : null });
  const choose = (row: (typeof trainers)[number], checked: boolean) => {
    if (!canEdit || checked && !eligible(row)) return;
    setDrafts(previous => {
      const next = { ...previous };
      if (checked) next[row.trainerId] = { trainerId: row.trainerId, fingerprint: row.fingerprint, acknowledgePreviousFix: false };
      else delete next[row.trainerId];
      return next;
    });
    setConfirmed(false); setFailed(false);
  };
  const clear = () => { setDrafts({}); setConfirmed(false); setFailed(false); };
  const customAcknowledged = customContext === workflow?.contextFingerprint;
  const canStage = canEdit && dirty && !stale && confirmed && choices.length <= 512
    && (!workflow?.customAiScripts || customAcknowledged)
    && choices.every(choice => !trainers.find(row => row.trainerId === choice.trainerId)?.previouslyFixed || choice.acknowledgePreviousFix);
  const stage = async () => {
    if (!canStage || isStaging || !workflow) return;
    const submitted = { ...drafts };
    setFailed(false);
    try {
      if (!await onStage(Object.values(submitted), workflow.contextFingerprint, customAcknowledged)) { setFailed(true); return; }
      setDrafts(previous => {
        const next = { ...previous };
        for (const [id, choice] of Object.entries(submitted)) if (next[Number(id)] === choice) delete next[Number(id)];
        return next;
      });
    } catch { setFailed(true); }
  };
  const status = (row: (typeof trainers)[number]) => pending.has(row.trainerId) ? t('fixAiFlags.staged') :
    row.previouslyFixed ? t(row.changedSinceFix ? 'fixAiFlags.changed' : 'fixAiFlags.fixed') :
    row.candidate ? t('fixAiFlags.candidate') : (row.currentFlags & 0x1000) !== 0 ? t('fixAiFlags.switchingSet') : t('fixAiFlags.noMismatch');
  return <FocusedEditorWorkspace className="fix-ai-flags-editor">
    <section className="panel wide-panel fix-ai-flags-panel" aria-labelledby="fix-ai-flags-title">
      <div className="fix-ai-flags-heading"><div><h2 id="fix-ai-flags-title"><Wrench size={22} aria-hidden="true" />{t('fixAiFlags.title')}</h2>
        <p>{t('fixAiFlags.subtitle')}</p></div>
        <button type="button" className="secondary-button" onClick={onRefresh} disabled={isLoading || isStaging}>
          <RefreshCw size={16} aria-hidden="true" />{translateLiteral('Refresh')}</button></div>
      <details className="fix-ai-flags-info"><summary>{t('fixAiFlags.why')}</summary><p>{t('fixAiFlags.explanation')}</p><p>{t('fixAiFlags.scope')}</p><p>{t('fixAiFlags.stampHelp')}</p></details>
      {trainers.some(row => row.previouslyFixed) ? <p className="fix-ai-flags-notice" role="status"><CheckCircle2 size={18} aria-hidden="true" />{t('fixAiFlags.previousWarning')}</p> : null}
      {workflow?.customAiScripts ? <div className="fix-ai-flags-notice"><p>{t('fixAiFlags.customWarning')}</p>
        <label className="fix-ai-flags-check"><input type="checkbox" checked={customAcknowledged} disabled={!canEdit}
          onChange={event => setCustomContext(event.target.checked ? workflow.contextFingerprint : null)} />{t('fixAiFlags.customAck')}</label></div> : null}
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('fixAiFlags.title')} onStart={onStartEditSession} readOnlyReason={t('fixAiFlags.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button type="button" className="primary-button" disabled={!canStage || isStaging} onClick={() => void stage()} aria-busy={isStaging || undefined}>
          <Save size={16} aria-hidden="true" />{translateLiteral(isStaging ? 'Staging' : 'Stage')}</button>
        <button type="button" className="danger-button" disabled={isStaging} onClick={() => onCancelEditSession(clear)}>
          <X size={16} aria-hidden="true" />{translateLiteral('Cancel')}</button>
        <span>{t('fixAiFlags.selectedCount', { count: choices.length })}</span>
      </EditorSessionBarActions> : null}
      <div className="fix-ai-flags-toolbar">
        <label className="fix-ai-flags-search"><Search size={16} aria-hidden="true" /><span className="sr-only">{t('fixAiFlags.search')}</span>
          <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={t('fixAiFlags.search')} /></label>
        <SearchableOptionInput ariaLabel={t('fixAiFlags.filter')} disabled={false} isFiniteCatalog localizeOptions={false} value={filter} onChange={setFilter}
          options={['candidates', 'reviewed', 'fixed', 'all'].map(value => ({ value, label: t(`fixAiFlags.filter.${value}`) }))} />
        <button type="button" className="secondary-button" disabled={!canEdit || !visible.some(eligible)} onClick={() => {
          setDrafts(previous => ({ ...previous, ...Object.fromEntries(visible.filter(eligible).map(row => [row.trainerId,
            { trainerId: row.trainerId, fingerprint: row.fingerprint, acknowledgePreviousFix: false }])) }));
          setConfirmed(false); setFailed(false);
        }}>{t('fixAiFlags.selectVisible')}</button>
        <button type="button" className="secondary-button" disabled={!dirty} onClick={clear}>{t('fixAiFlags.clear')}</button>
      </div>
      <p>{t('fixAiFlags.counts', { candidates: trainers.filter(row => row.candidate).length, total: trainers.length })}</p>
      {dirty ? <label className="fix-ai-flags-check fix-ai-flags-confirm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />{t('fixAiFlags.confirm')}</label> : null}
      {stale ? <p className="fix-ai-flags-notice" role="status">{t('fixAiFlags.stale')}</p> : null}
      {choices.length > 512 ? <p className="fix-ai-flags-notice">{t('fixAiFlags.limit')}</p> : null}
      <div className="fix-ai-flags-columns">
        <div className="fix-ai-flags-list" role="region" aria-label={t('fixAiFlags.trainers')} tabIndex={0}>
          {visible.map(row => <div key={row.trainerId} className={`fix-ai-flags-row${selected?.trainerId === row.trainerId ? ' selected-row' : ''}`}>
            <label className="fix-ai-flags-check"><input type="checkbox" checked={Boolean(drafts[row.trainerId])} disabled={!canEdit || !eligible(row) && !drafts[row.trainerId]}
              onChange={event => { choose(row, event.target.checked); setSelectedId(row.trainerId); }} />
              <span className="sr-only">{t('fixAiFlags.selectTrainer', { id: row.trainerId, name: row.name })}</span></label>
            <button type="button" className="fix-ai-flags-row-button" onClick={() => setSelectedId(row.trainerId)} aria-pressed={selected?.trainerId === row.trainerId}>
              <span data-localization-ignore="true">{row.trainerId}. {row.name}</span><span className="fix-ai-flags-status">{status(row)}</span></button>
            <div className="fix-ai-flags-masks"><code>{hex(row.currentFlags)}</code>{row.proposedFlags !== null ? <><span aria-label={t('fixAiFlags.proposed')}>→</span><code>{hex(row.proposedFlags)}</code></> : null}</div>
          </div>)}
          {visible.length === 0 ? <p className="empty-state">{t('fixAiFlags.noMatches')}</p> : null}
        </div>
        <section className="fix-ai-flags-detail" aria-labelledby="fix-ai-flags-detail-title">
          <h3 id="fix-ai-flags-detail-title">{t('fixAiFlags.details')}</h3>
          {selected ? <><strong data-localization-ignore="true">{selected.trainerId}. {selected.name}</strong>
            <p>{status(selected)}</p><code className="fix-ai-flags-path" data-localization-ignore="true">{selected.sourceFile}</code>
            <dl className="fix-ai-flags-values"><div><dt>{t('fixAiFlags.current')}</dt><dd><code>{hex(selected.currentFlags)}</code></dd></div>
              <div><dt>{t('fixAiFlags.proposed')}</dt><dd><code>{selected.proposedFlags === null ? translateLiteral('None') : hex(selected.proposedFlags)}</code></dd></div>
              <div><dt>{t('fixAiFlags.base')}</dt><dd><code>{selected.baseFlags === null ? translateLiteral('Unknown') : hex(selected.baseFlags)}</code></dd></div></dl>
            {selected.candidate ? <p>{t('fixAiFlags.change')}</p> : <p>{t('fixAiFlags.skip')}</p>}
            {pending.has(selected.trainerId) ? <p className="fix-ai-flags-notice">{t('fixAiFlags.pendingHelp')}</p> : null}
            {selected.previouslyFixed ? <><p className="fix-ai-flags-notice">{t('fixAiFlags.fixedHelp', { date: selected.fixedAtUtc ? new Date(selected.fixedAtUtc).toLocaleString(formatLocale) : translateLiteral('Unknown') })}</p>
              {selected.changedSinceFix ? <p>{t('fixAiFlags.changedHelp')}</p> : null}
              {drafts[selected.trainerId] ? <label className="fix-ai-flags-check"><input type="checkbox" checked={drafts[selected.trainerId].acknowledgePreviousFix}
                onChange={event => { const checked = event.target.checked; setDrafts(previous => ({ ...previous,
                  [selected.trainerId]: { ...previous[selected.trainerId], acknowledgePreviousFix: checked } })); setConfirmed(false); }} />{t('fixAiFlags.previousAck')}</label> : null}</> : null}
          </> : <p>{t('fixAiFlags.noMatches')}</p>}
        </section>
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
