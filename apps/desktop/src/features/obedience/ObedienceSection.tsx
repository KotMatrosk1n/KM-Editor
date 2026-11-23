/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { Award, RefreshCw, RotateCcw, Save, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { defaultObedienceSelections, getObediencePendingSelections, obedienceSelectionsSchema,
  type ObedienceWorkflow } from '../../bridge/obedienceContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './ObedienceSection.css';

type Props = {
  workflow: ObedienceWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (selections: number[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};

export function ObedienceSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = getObediencePendingSelections(session);
  const clean = (pending ?? workflow?.selections ?? []).map(String);
  const [draft, setDraft] = useState<string[] | null>(null);
  const draftRef = useRef(draft); draftRef.current = draft;
  const [failed, setFailed] = useState(false);
  const desired = draft ?? clean;
  const values = desired.map(value => /^\d{1,3}$/.test(value) ? Number(value) : NaN);
  const valid = obedienceSelectionsSchema.safeParse(values).success;
  const dirty = draft !== null && draft.join(',') !== clean.join(',');
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.obedience', field: 'selections',
    message: dirty && !valid ? t('obedience.invalid') : failed ? t('obedience.failed') : null });
  const stage = async () => {
    if (!canEdit || !dirty || !valid || isStaging) return;
    const submitted = draft;
    setFailed(false);
    try {
      if (!await onStage(values)) { setFailed(true); return; }
      if (draftRef.current === submitted) setDraft(null);
    } catch { setFailed(true); }
  };
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel obedience-editor" aria-labelledby="obedience-heading">
      <div className="panel-heading"><Award size={18} aria-hidden="true" />
        <h2 id="obedience-heading">{t('obedience.title')}</h2>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <p>{t('obedience.subtitle')}</p>
      <p id="obedience-help">{t('obedience.help')}</p>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('obedience.title')} onStart={onStartEditSession} readOnlyReason={t('obedience.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="danger-button" type="button" disabled={!canEdit || desired.join(',') === defaultObedienceSelections.join(',')}
          onClick={() => { setDraft(defaultObedienceSelections.map(String)); setFailed(false); }}>
          <RotateCcw size={16} aria-hidden="true" /><span>{translateLiteral('Restore to Vanilla')}</span>
        </button>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || !valid || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDraft(null); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'obedience.draft' : pending ? 'obedience.staged' : 'obedience.clean')}</span>
      </EditorSessionBarActions> : null}
      <div className="obedience-grid editor-property-ledger">
        <div className="obedience-ledger-header">{t('obedience.level')}</div>
        {desired.map((value, index) => <label className="obedience-card" key={index}>
          <span>{t('obedience.badges', { count: index })}</span>
          <input type="text" inputMode="numeric" value={value} disabled={!canEdit}
            aria-label={t('obedience.limitLabel', { count: index })} aria-describedby="obedience-help"
            aria-invalid={!Number.isInteger(values[index]) || values[index] < 1 || values[index] > 100 || undefined}
            onChange={event => { const next = [...desired]; next[index] = event.target.value; setDraft(next); setFailed(false); }} />
          <small>{t('obedience.vanilla', { level: defaultObedienceSelections[index] })}</small>
        </label>)}
      </div>
      <p>{t('obedience.saveHelp')}</p>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
