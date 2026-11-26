/* SPDX-License-Identifier: GPL-3.0-only */
import { ContextHelp } from '../../components/ContextHelp';
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
import '../../components/editorRecordTable.css';
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
        <ContextHelp label={t('obedience.title')}>
          <p>{t('obedience.subtitle')}</p>
          <p>{t('obedience.saveHelp')}</p>
        </ContextHelp>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
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
      <span id="obedience-help" hidden>{t('obedience.help')}</span>
      <table className="editor-record-table obedience-table" aria-label={t('obedience.title')}>
        <thead><tr>
          <th scope="col">{translateLiteral('Badges')}</th>
          <th scope="col">
            <span className="context-help-heading">
              {t('obedience.level')}
              <ContextHelp label={t('obedience.level')}>{t('obedience.help')}</ContextHelp>
            </span>
          </th>
          <th scope="col">{translateLiteral('Vanilla')}</th>
        </tr></thead>
        <tbody>
          {desired.map((value, index) => (
            <tr key={index}>
              <th scope="row">{t('obedience.badges', { count: index })}</th>
              <td><input type="text" inputMode="numeric" value={value} disabled={!canEdit}
                aria-label={t('obedience.limitLabel', { count: index })} aria-describedby="obedience-help"
                aria-invalid={!Number.isInteger(values[index]) || values[index] < 1 || values[index] > 100 || undefined}
                onChange={event => {
                  const next = [...desired];
                  next[index] = event.target.value;
                  setDraft(next);
                  setFailed(false);
                }} /></td>
              <td>{defaultObedienceSelections[index]}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
