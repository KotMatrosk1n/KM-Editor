/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { RefreshCw, RotateCcw, Save, Sparkles, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { defaultHeldItemRates, getHeldItemPendingRates, parseHeldItemRateDrafts,
  type HeldItemChanceWorkflow } from '../../bridge/heldItemChanceContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './HeldItemChanceSection.css';

type Props = {
  workflow: HeldItemChanceWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (rates: number[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};

export function HeldItemChanceSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = getHeldItemPendingRates(session);
  const clean = (pending ?? workflow?.rates ?? []).map(String);
  const [draft, setDraft] = useState<string[] | null>(null);
  const draftRef = useRef(draft); draftRef.current = draft;
  const [failed, setFailed] = useState(false);
  const desired = draft ?? clean;
  const rates = parseHeldItemRateDrafts(desired);
  const dirty = draft !== null && draft.join(',') !== clean.join(',');
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  const invalid = dirty && rates === null;
  usePublishCommonEditorError({ domain: 'workflow.heldItemChance', field: 'rates',
    message: invalid ? t('heldItemChance.invalid') : failed ? t('heldItemChance.failed') : null });
  const stage = async () => {
    if (!canEdit || !dirty || isStaging || rates === null) return;
    const submitted = draft;
    setFailed(false);
    try {
      if (!await onStage(rates)) { setFailed(true); return; }
      if (draftRef.current === submitted) setDraft(null);
    } catch { setFailed(true); }
  };
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel held-item-chance" aria-labelledby="held-item-chance-heading">
      <div className="panel-heading"><Sparkles size={18} aria-hidden="true" />
        <h2 id="held-item-chance-heading">{t('heldItemChance.title')}</h2>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <p>{t('heldItemChance.subtitle')}</p>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('heldItemChance.title')} onStart={onStartEditSession} readOnlyReason={t('heldItemChance.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="danger-button" type="button" disabled={!canEdit || desired.join(',') === defaultHeldItemRates.join(',')}
          onClick={() => { setDraft(defaultHeldItemRates.map(String)); setFailed(false); }}>
          <RotateCcw size={16} aria-hidden="true" /><span>{translateLiteral('Restore to Vanilla')}</span>
        </button>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || rates === null || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDraft(null); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'heldItemChance.draft' : pending ? 'heldItemChance.staged' : 'heldItemChance.clean')}</span>
      </EditorSessionBarActions> : null}
      <div className="held-item-rate-groups">
        {[0, 1].map(group => {
          const values = desired.slice(group * 3, group * 3 + 3);
          const validFields = values.length === 3 && values.every(value => /^\d{1,3}$/.test(value) && Number(value) <= 100);
          const total = validFields ? values.reduce((sum, value) => sum + Number(value), 0) : null;
          const groupInvalid = total === null || total > 100;
          return <fieldset key={group} className="held-item-rate-group">
            <legend>{t(group === 0 ? 'heldItemChance.normal' : 'heldItemChance.boosted')}</legend>
            <p>{t(group === 0 ? 'heldItemChance.normalHelp' : 'heldItemChance.boostedHelp')}</p>
            <div className="held-item-slot-grid">
              {[0, 1, 2].map(slot => <label key={slot} className="held-item-rate-field">
                <span>{t('heldItemChance.slot', { number: slot + 1 })}</span>
                <span className="held-item-rate-input"><input type="text" inputMode="numeric"
                  aria-label={`${t(group === 0 ? 'heldItemChance.normal' : 'heldItemChance.boosted')} ${t('heldItemChance.slot', { number: slot + 1 })}`}
                  aria-invalid={dirty && groupInvalid || undefined} aria-describedby="held-item-rate-limits"
                  disabled={!canEdit} value={desired[group * 3 + slot] ?? ''}
                  onChange={event => {
                    const next = [...desired]; next[group * 3 + slot] = event.target.value; setDraft(next); setFailed(false);
                  }} /><span aria-hidden="true">%</span></span>
              </label>)}
            </div>
            <p className="held-item-total" aria-live="polite">{total === null ? t('heldItemChance.incomplete') :
              t('heldItemChance.total', { total, remaining: Math.max(0, 100 - total) })}</p>
            {dirty && total !== null && total > 100 ? <p role="alert">{t('heldItemChance.overTotal')}</p> : null}
          </fieldset>;
        })}
      </div>
      <div className="held-item-notes">
        <p id="held-item-rate-limits">{t('heldItemChance.limits')}</p>
        <p>{t('heldItemChance.itemsHelp')}</p>
        <p>{t('heldItemChance.equalHelp')}</p>
        <p>{t('heldItemChance.scopeHelp')}</p>
        <p>{t('heldItemChance.packageHelp', { editor: t('gameplaySettings.title') })}</p>
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
