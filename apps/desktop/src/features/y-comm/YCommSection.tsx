/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { WifiOff, RefreshCw, RotateCcw, Save, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { getYCommPendingDisabled, type YCommWorkflow } from '../../bridge/yCommContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization';
import './YCommSection.css';

type Props = {
  workflow: YCommWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (disabled: boolean) => Promise<boolean>; onDirtyStateChange: (dirty: boolean) => void;
  onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};

export function YCommSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = getYCommPendingDisabled(session);
  const clean = pending ?? workflow?.disabled ?? false;
  const [draft, setDraft] = useState<boolean | null>(null);
  const draftRef = useRef(draft); draftRef.current = draft;
  const [failed, setFailed] = useState(false);
  const desired = draft ?? clean;
  const needsRepair = workflow?.needsRepair === true && pending === null;
  const dirty = draft !== null && draft !== clean || isEditing && needsRepair;
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.yComm', field: 'disabled', message: failed ? t('yComm.failed') : null });
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
    <section className="panel wide-panel y-comm-editor" aria-labelledby="y-comm-heading">
      <div className="panel-heading"><WifiOff size={20} aria-hidden="true" />
        <h2 id="y-comm-heading">{t('yComm.title')}</h2>
        <ContextHelp label={t('yComm.title')}><p>{t('yComm.subtitle')}</p><p>{t('yComm.saveHelp')}</p></ContextHelp>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('yComm.title')} onStart={onStartEditSession} readOnlyReason={t('yComm.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="danger-button" type="button" disabled={!canEdit || !desired && !needsRepair}
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
        <span className="draft-action-summary">{t(dirty ? 'yComm.draft' : pending !== null ? 'yComm.staged' : 'yComm.clean')}</span>
      </EditorSessionBarActions> : null}
      <div className="editor-setting-row editor-record-context">
        <div className="editor-record-action-rail">
          <label className="y-comm-setting">
            <input type="checkbox" checked={desired} disabled={!canEdit}
              onChange={event => { setDraft(event.target.checked); setFailed(false); }} />
            <strong>{t('yComm.title')}</strong>
          </label>
          <ContextHelp label={t('yComm.title')}>{t('yComm.help')}</ContextHelp>
        </div>
        <p role="status">{t('yComm.current', { value: t(workflow?.disabled == null ? 'yComm.unavailable'
          : workflow.disabled ? 'yComm.disabled' : 'yComm.enabled') })}</p>
      </div>
      {needsRepair ? <p role="status">{t('yComm.repair')}</p> : null}
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
