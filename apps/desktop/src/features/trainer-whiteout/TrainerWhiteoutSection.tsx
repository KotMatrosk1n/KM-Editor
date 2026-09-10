/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useRef, useState } from 'react';
import { RefreshCw, RotateCcw, Save, Search, UsersRound, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { type TrainerWhiteoutChange, type TrainerWhiteoutWorkflow } from '../../bridge/trainerWhiteoutContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './TrainerWhiteoutSection.css';

type Props = {
  workflow: TrainerWhiteoutWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (changes: TrainerWhiteoutChange[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};

export function TrainerWhiteoutSection({ workflow, session, isEditing, isEditStarting, isStaging,
  isLoading, onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [drafts, setDrafts] = useState<Record<number, boolean | null>>({});
  const draftsRef = useRef(drafts); draftsRef.current = drafts;
  const [failed, setFailed] = useState(false);
  const trainers = workflow?.trainers ?? [];
  const pending = useMemo(() => new Map(session?.pendingEdits.filter(edit => edit.domain === 'workflow.trainerWhiteout')
    .map(edit => [Number(edit.recordId), edit.newValue === 'vanilla' ? null : edit.newValue === 'true']) ?? []), [session]);
  const clean = (row: (typeof trainers)[number]) => pending.has(row.trainerId) ? pending.get(row.trainerId)! : row.override;
  const setting = (row: (typeof trainers)[number]) => row.trainerId in drafts ? drafts[row.trainerId] : clean(row);
  const value = (row: (typeof trainers)[number]) => setting(row) ?? row.vanillaEnabled;
  const changed = (row: (typeof trainers)[number]) => row.trainerId in drafts &&
    (drafts[row.trainerId] !== clean(row) || row.mixed && !pending.has(row.trainerId));
  const changes = trainers.filter(changed).map(row => ({ trainerId: row.trainerId, enabled: drafts[row.trainerId] }));
  const dirty = changes.length > 0;
  const selected = trainers.find(row => row.trainerId === selectedId) ?? trainers[0];
  const visible = trainers.filter(row => `${row.trainerId} ${row.name}`.toLocaleLowerCase().includes(query.toLocaleLowerCase().trim()));
  const editable = trainers;
  const canEdit = isEditing && workflow?.canEdit === true;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.trainerWhiteout', field: 'enabled', message: failed ? t('trainerWhiteout.failed') : null });
  const setValue = (id: number, enabled: boolean | null) => {
    const row = trainers.find(row => row.trainerId === id);
    if (!canEdit || !row) return;
    setDrafts(previous => ({ ...previous, [id]: enabled }));
    setFailed(false);
  };
  const setAll = (enabled: boolean) => {
    if (!canEdit) return;
    setDrafts(previous => ({ ...previous, ...Object.fromEntries(editable.map(row => [row.trainerId, enabled])) }));
    setFailed(false);
  };
  const stage = async () => {
    if (!canEdit || !dirty || isStaging) return;
    const submitted = changes;
    setFailed(false);
    try {
      if (!await onStage(submitted)) { setFailed(true); return; }
      setDrafts(previous => {
        const next = { ...previous };
        for (const change of submitted) if (draftsRef.current[change.trainerId] === change.enabled) delete next[change.trainerId];
        return next;
      });
    } catch { setFailed(true); }
  };
  const stateLabel = (enabled: boolean) => translateLiteral(enabled ? 'Yes' : 'No');
  return <FocusedEditorWorkspace className="trainer-whiteout-editor">
    <section className="panel wide-panel trainer-whiteout-panel" aria-labelledby="trainer-whiteout-title">
      <div className="panel-heading trainer-whiteout-heading">
        <div className="trainer-whiteout-title"><UsersRound size={22} aria-hidden="true" /><div>
          <h2 id="trainer-whiteout-title">{t('trainerWhiteout.title')} <span className="status-pill status-pill-info">Beta</span></h2>
          <p>{t('trainerWhiteout.subtitle')}</p></div></div>
        <button type="button" className="secondary-button compact-button" onClick={onRefresh} disabled={isLoading || isStaging}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span></button>
      </div>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('trainerWhiteout.title')} onStart={onStartEditSession} readOnlyReason={t('trainerWhiteout.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button type="button" className="primary-button" disabled={!canEdit || !dirty || isStaging} onClick={() => void stage()} aria-busy={isStaging || undefined}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span></button>
        <button type="button" className="danger-button" disabled={isStaging} onClick={() => onCancelEditSession(() => { setDrafts({}); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span></button>
        <span className="draft-action-summary">{t('trainerWhiteout.drafts', { count: changes.length })}</span>
      </EditorSessionBarActions> : null}
      <div className="trainer-whiteout-toolbar">
        <label className="trainer-whiteout-search"><Search size={16} aria-hidden="true" /><span className="sr-only">{t('trainerWhiteout.search')}</span>
          <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={t('trainerWhiteout.search')} /></label>
        <div className="trainer-whiteout-bulk">
          <button type="button" className="secondary-button" disabled={!canEdit || editable.length === 0} onClick={() => setAll(true)}>{t('trainerWhiteout.enableAll')}</button>
          <button type="button" className="secondary-button" disabled={!canEdit || editable.length === 0} onClick={() => setAll(false)}>{t('trainerWhiteout.disableAll')}</button>
        </div>
      </div>
      <p className="trainer-whiteout-coverage">{t('trainerWhiteout.coverage', { editable: editable.length, total: trainers.length })}</p>
      <div className="trainer-whiteout-columns">
        <div className="trainer-whiteout-table-scroll" tabIndex={0} role="region" aria-label={t('trainerWhiteout.trainers')}>
          <table className="data-table trainer-whiteout-table"><thead><tr><th scope="col">ID</th><th scope="col">{t('trainerWhiteout.trainer')}</th><th scope="col">{t('trainerWhiteout.enabled')}</th></tr></thead>
            <tbody>{visible.map(row => <tr key={row.trainerId} className={selected?.trainerId === row.trainerId ? 'selected-row' : undefined}>
              <td>{row.trainerId}</td><td><button type="button" className="trainer-whiteout-row-button" aria-pressed={selected?.trainerId === row.trainerId}
                onClick={() => setSelectedId(row.trainerId)} data-localization-ignore="true">{row.name}</button></td>
              <td><span>{stateLabel(value(row))}</span>{row.mixed && !pending.has(row.trainerId) && !(row.trainerId in drafts) ? <span className="status-pill status-pill-info">{t('trainerWhiteout.mixed')}</span> : null}{changed(row) ? <span className="status-pill status-pill-info">{t('trainerWhiteout.draft')}</span> : pending.has(row.trainerId) ? <span className="status-pill status-pill-info">{t('trainerWhiteout.staged')}</span> : null}</td>
            </tr>)}</tbody>
          </table>
          {visible.length === 0 ? <p className="empty-state">{t('trainerWhiteout.noMatches')}</p> : null}
        </div>
        <section className="trainer-whiteout-detail" aria-labelledby="trainer-whiteout-detail-title">
          {selected ? <>
            <div className="trainer-whiteout-detail-heading"><div><span className="eyebrow">{t('trainerWhiteout.trainer')} {selected.trainerId}</span>
              <h3 id="trainer-whiteout-detail-title" data-localization-ignore="true">{selected.name}</h3></div>
              <button type="button" className="secondary-button" disabled={!canEdit}
                onClick={() => setValue(selected.trainerId, null)}>
                <RotateCcw size={16} aria-hidden="true" /><span>{t('trainerWhiteout.reset')}</span></button></div>
            <div className="trainer-whiteout-field"><span>{t('trainerWhiteout.enabled')}</span>
              <SearchableOptionInput ariaLabel={t('trainerWhiteout.enabled')} isFiniteCatalog localizeOptions={false}
                value={String(value(selected))} disabled={!canEdit}
                options={[{ value: 'true', label: translateLiteral('Yes') }, { value: 'false', label: translateLiteral('No') }]}
                onChange={value => setValue(selected.trainerId, value === 'true')}
                onReselect={value => setValue(selected.trainerId, value === 'true')} />
            </div>
            <p>{t('trainerWhiteout.help')}</p>
            {selected.mixed ? <p className="trainer-whiteout-notice">{t('trainerWhiteout.mixedHelp')}</p> : null}
            <dl className="trainer-whiteout-values"><div><dt>{t('trainerWhiteout.current')}</dt><dd>{stateLabel(selected.enabled)}</dd></div>
              <div><dt>{t('trainerWhiteout.vanilla')}</dt><dd>{stateLabel(selected.vanillaEnabled)}</dd></div></dl>
          </> : <p id="trainer-whiteout-detail-title">{t('trainerWhiteout.noTrainers')}</p>}
        </section>
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
