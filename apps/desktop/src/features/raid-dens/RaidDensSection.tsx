/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useRef, useState } from 'react';
import { Gem, HandCoins, Mountain, RefreshCw, Save, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { type RaidDensWorkflow } from '../../bridge/raidDensContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './RaidDensSection.css';

type Props = {
  workflow: RaidDensWorkflow | null;
  session: EditSession | null;
  isEditing: boolean;
  isEditStarting: boolean;
  isStaging: boolean;
  isLoading: boolean;
  onStartEditSession: () => void;
  onCancelEditSession: (discard: () => void) => void;
  onStage: (disabled: boolean) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void;
  onRefresh: () => void;
  panelOutput: WorkflowPanelOutput;
};

export function RaidDensSection({ workflow, session, isEditing, isEditStarting, isStaging,
  isLoading, onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh,
  panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = session?.pendingEdits.find(edit => edit.domain === 'workflow.raidDens' &&
    edit.recordId === 'den-interaction' && edit.field === 'disabled');
  const clean = pending?.newValue === 'true' ? true : pending?.newValue === 'false' ? false : workflow?.disabled ?? false;
  const [draft, setDraft] = useState<boolean | null>(null);
  const draftRef = useRef(draft);
  draftRef.current = draft;
  const [failed, setFailed] = useState(false);
  const desired = draft ?? clean;
  const dirty = draft !== null && draft !== clean;
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.raidDens', field: 'disabled',
    message: failed ? t('raidDens.failed') : null });

  const stage = async () => {
    if (!canEdit || !dirty || isStaging) return;
    const submitted = desired;
    setFailed(false);
    const accepted = await onStage(submitted);
    if (!accepted) { setFailed(true); return; }
    // A delayed response only acknowledges its own submitted value.
    if (draftRef.current === submitted) setDraft(null);
  };
  const stateLabel = workflow?.disabled === null || !workflow ? t('raidDens.unavailable') :
    t(workflow.disabled ? 'raidDens.disabled' : 'raidDens.enabled');

  return <FocusedEditorWorkspace className="raid-dens-editor">
    <section className="panel wide-panel raid-dens-panel" aria-labelledby="raid-dens-heading">
      <div className="panel-heading raid-dens-heading">
        <div className="raid-dens-title"><Mountain size={22} aria-hidden="true" />
          <div><h2 id="raid-dens-heading">{t('raidDens.title')}</h2><p>{t('raidDens.subtitle')}</p></div>
        </div>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" className={isLoading ? 'button-busy-icon' : undefined} />
          <span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing}
        isStarting={isEditStarting} label={t('raidDens.title')} onStart={onStartEditSession}
        readOnlyReason={t('raidDens.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDraft(null); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'raidDens.draft' : pending ? 'raidDens.staged' : 'raidDens.clean')}</span>
      </EditorSessionBarActions> : null}

      <div className="raid-dens-content">
        <div className="raid-dens-setting">
          <div className="raid-dens-setting-copy"><h3>{t('raidDens.interaction')}</h3><p>{t('raidDens.description')}</p></div>
          <label className={`raid-dens-toggle${desired ? ' raid-dens-toggle-selected' : ''}`}>
            <input type="checkbox" checked={desired} disabled={!canEdit}
              onChange={event => { setDraft(event.target.checked); setFailed(false); }} />
            <span><strong>{t('raidDens.disable')}</strong><span>{t('raidDens.toggleHelp')}</span></span>
          </label>
          <div className="raid-dens-current" role="status"><span>{t('raidDens.current')}</span>
            <strong className={`status-pill ${workflow?.disabled === null || !workflow ? 'status-blocked' : 'status-pill-info'}`}>{stateLabel}</strong>
            {pending ? <span>{t('raidDens.stagedValue', { value: t(clean ? 'raidDens.disabled' : 'raidDens.enabled') })}</span> : null}
          </div>
        </div>

        <div className="raid-dens-effects-region">
          <h3>{t('raidDens.effects')}</h3>
          <div className="raid-dens-effects">
          <article><Mountain size={24} aria-hidden="true" /><h3>{t('raidDens.raids')}</h3><p>{t('raidDens.raidsHelp')}</p></article>
          <article><HandCoins size={24} aria-hidden="true" /><h3>{t('raidDens.watts')}</h3><p>{t('raidDens.wattsHelp')}</p></article>
          <article><Gem size={24} aria-hidden="true" /><h3>{t('raidDens.pieces')}</h3><p>{t('raidDens.piecesHelp')}</p></article>
          </div>
        </div>
        <div className="raid-dens-scope"><h3>{t('raidDens.scope')}</h3><p>{t('raidDens.scopeHelp')}</p>
          <p>{t('raidDens.restoreHelp')}</p></div>
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
