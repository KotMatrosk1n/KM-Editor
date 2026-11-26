/* SPDX-License-Identifier: GPL-3.0-only */
import { ContextHelp } from '../../components/ContextHelp';
import { useEffect, useMemo, useRef, useState } from 'react';
import { RefreshCw, RotateCcw, Save, Search, Skull, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { type TrainerWhiteoutChange, type TrainerWhiteoutWorkflow } from '../../bridge/trainerWhiteoutContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { EditorRecordTable } from '../../components/EditorRecordTable';
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
        <div className="trainer-whiteout-title"><Skull size={22} aria-hidden="true" /><div>
          <h2 id="trainer-whiteout-title">{t('trainerWhiteout.title')} <span className="status-pill status-pill-info">Beta</span></h2>
          </div><ContextHelp label={t('trainerWhiteout.title')}>{t('trainerWhiteout.subtitle')}</ContextHelp></div>
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
      <p className="trainer-whiteout-coverage">{t('trainerWhiteout.coverage', { editable: editable.length, total: trainers.length })}</p>
      <RecordEditorWorkspace id="trainer-whiteout" className="trainer-whiteout-layout" initialSection="whiteout"
        browserControls={<div className="trainer-whiteout-toolbar">
        <label className="trainer-whiteout-search"><Search size={16} aria-hidden="true" /><span className="sr-only">{t('trainerWhiteout.search')}</span>
          <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={t('trainerWhiteout.search')} /></label>
        <div className="trainer-whiteout-bulk">
          <button type="button" className="secondary-button" disabled={!canEdit || editable.length === 0} onClick={() => setAll(true)}>{t('trainerWhiteout.enableAll')}</button>
          <button type="button" className="secondary-button" disabled={!canEdit || editable.length === 0} onClick={() => setAll(false)}>{t('trainerWhiteout.disableAll')}</button>
        </div>
      </div>}
        browser={<EditorRecordTable label={t('trainerWhiteout.trainers')} identityLabel={t('trainerWhiteout.trainer')}
          rows={visible} rowId={row => String(row.trainerId)} identity={row => <span data-localization-ignore="true">{row.name}</span>}
          selection={String(selected?.trainerId)} onSelect={row => setSelectedId(row.trainerId)}
          empty={t('trainerWhiteout.noMatches')}
          columns={[
            { id: 'id', label: 'ID', value: row => row.trainerId },
            { id: 'enabled', label: t('trainerWhiteout.enabled'), value: row => stateLabel(value(row)) },
            { id: 'status', label: translateLiteral('Status'), value: row => changed(row) ? t('trainerWhiteout.draft')
              : pending.has(row.trainerId) ? t('trainerWhiteout.staged') : row.mixed ? t('trainerWhiteout.mixed') : null }
          ]} />}>
        {selected ? <>
          <EditorWorkspaceHeader afterBrowser><EditorRecordHeading id={selected.trainerId} name={selected.name} /></EditorWorkspaceHeader>
          <EditorWorkspaceSection id="whiteout" label={t('trainerWhiteout.enabled')} summary={stateLabel(value(selected))}>
            <div className="trainer-whiteout-detail">
              <div className="editor-record-context">
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
            </div>
          </EditorWorkspaceSection>
        </> : <p>{t('trainerWhiteout.noTrainers')}</p>}
      </RecordEditorWorkspace>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
