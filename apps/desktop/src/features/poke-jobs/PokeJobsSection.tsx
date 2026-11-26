/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { BriefcaseBusiness, RefreshCw, RotateCcw, Save, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { getPokeJobsPendingDisabled, type PokeJobsWorkflow } from '../../bridge/pokeJobsContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization';
import './PokeJobsSection.css';

type Props = {
  workflow: PokeJobsWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (disabled: boolean) => Promise<boolean>; onDirtyStateChange: (dirty: boolean) => void;
  onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};

export function PokeJobsSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = getPokeJobsPendingDisabled(session);
  const clean = pending ?? workflow?.disabled ?? false;
  const [draft, setDraft] = useState<boolean | null>(null);
  const draftRef = useRef(draft); draftRef.current = draft;
  const [failed, setFailed] = useState(false);
  const desired = draft ?? clean;
  const dirty = draft !== null && draft !== clean;
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.pokeJobs', field: 'disabled', message: failed ? t('pokeJobs.failed') : null });
  const stage = async () => {
    if (!canEdit || !dirty || isStaging) return;
    const submitted = draft;
    setFailed(false);
    try {
      if (!await onStage(desired)) { setFailed(true); return; }
      if (draftRef.current === submitted) setDraft(null);
    } catch { setFailed(true); }
  };
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel poke-jobs-editor" aria-labelledby="poke-jobs-heading">
      <div className="panel-heading"><BriefcaseBusiness size={20} aria-hidden="true" />
        <h2 id="poke-jobs-heading">{t('pokeJobs.title')}</h2>
        <ContextHelp label={t('pokeJobs.title')}><p>{t('pokeJobs.subtitle')}</p><p>{t('pokeJobs.saveHelp')}</p></ContextHelp>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('pokeJobs.title')} onStart={onStartEditSession} readOnlyReason={t('pokeJobs.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="danger-button" type="button" disabled={!canEdit || !desired}
          onClick={() => { setDraft(false); setFailed(false); }}>
          <RotateCcw size={16} aria-hidden="true" /><span>{translateLiteral('Restore to Vanilla')}</span>
        </button>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDraft(null); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'pokeJobs.draft' : pending !== null ? 'pokeJobs.staged' : 'pokeJobs.clean')}</span>
      </EditorSessionBarActions> : null}
      <div className="editor-setting-row editor-record-context">
        <div className="editor-record-action-rail">
          <label className="poke-jobs-setting">
            <input type="checkbox" checked={desired} disabled={!canEdit}
              onChange={event => { setDraft(event.target.checked); setFailed(false); }} />
            <strong>{t('pokeJobs.title')}</strong>
          </label>
          <ContextHelp label={t('pokeJobs.title')}>{t('pokeJobs.help')}</ContextHelp>
        </div>
        <p role="status">{t('pokeJobs.current', { value: t(workflow?.disabled == null ? 'pokeJobs.unavailable'
          : workflow.disabled ? 'pokeJobs.disabled' : 'pokeJobs.enabled') })}</p>
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
