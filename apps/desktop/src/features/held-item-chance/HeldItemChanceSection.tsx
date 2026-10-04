/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { PokemonSearchBox, PokemonTable, filterPokemonRecords, type PokemonSelectionRecord } from '../../components/PokemonSelection';
import { RefreshCw, RotateCcw, Save, Sparkles, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { getHeldItemPokemonState, parseHeldItemRateDrafts,
  type HeldItemChanceUpdate, type HeldItemChanceWorkflow } from '../../bridge/heldItemChanceContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './HeldItemChanceSection.css';

type Props = {
  formatPokemonName: (record: PokemonSelectionRecord) => string;
  workflow: HeldItemChanceWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (updates: HeldItemChanceUpdate[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};
type Draft = { rates: string[]; items: string[]; customRates: boolean };

export function HeldItemChanceSection({ formatPokemonName, workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [selectedId, setSelectedId] = useState<number>();
  const [searchText, setSearchText] = useState('');
  const [drafts, setDrafts] = useState<Record<number, Draft>>({});
  const draftsRef = useRef(drafts); draftsRef.current = drafts;
  const [failed, setFailed] = useState(false);
  const records = workflow?.pokemon.map(row => ({ ...getHeldItemPokemonState(row, session, workflow.rates), speciesId: row.species })) ?? [];
  const filtered = filterPokemonRecords(records, searchText, formatPokemonName);
  const selected = records.find(row => row.personalId === selectedId) ?? filtered[0];
  const clean = selected ? { rates: selected.rates.map(String), items: selected.items.map(String), customRates: selected.customRates } : null;
  const desired = selected ? drafts[selected.personalId] ?? clean : null;
  const options = workflow?.itemOptions ?? [];
  const updates: HeldItemChanceUpdate[] = [];
  let invalid = false;
  for (const [id, draft] of Object.entries(drafts)) {
    const row = records.find(row => row.personalId === Number(id));
    const rates = parseHeldItemRateDrafts(draft.rates);
    const validItems = draft.items.length === 3 && draft.items.every(value => /^\d+$/.test(value) && options.some(option => option.value === Number(value)));
    if (!row || draft.customRates && rates === null || !validItems) { invalid = true; continue; }
    if (row.customRates !== draft.customRates || draft.customRates && row.rates.join(',') !== rates!.join(',')
      || row.items.join(',') !== draft.items.map(Number).join(','))
      updates.push({ personalId: row.personalId, items: draft.items.map(Number), rates: draft.customRates ? rates : null });
  }
  const pendingIds = new Set([
    ...Object.keys(drafts).map(Number),
    ...(session?.pendingEdits.filter(edit => edit.domain === 'workflow.heldItemChance' || edit.domain === 'workflow.pokemon')
      .map(edit => Number(edit.recordId)) ?? [])
  ]);
  const dirty = invalid || updates.length > 0;
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.heldItemChance', field: 'rates',
    message: invalid ? t('heldItemChance.pokemonInvalid') : failed ? t('heldItemChance.failed') : null });
  const change = (next: Draft) => {
    if (!canEdit || !selected) return;
    setSelectedId(selected.personalId);
    setDrafts(current => ({ ...current, [selected.personalId]: next })); setFailed(false);
  };
  const stage = async () => {
    if (!canEdit || !dirty || isStaging || invalid) return;
    const submitted = draftsRef.current;
    setFailed(false);
    try {
      if (!await onStage(updates)) { setFailed(true); return; }
      setDrafts(current => Object.fromEntries(Object.entries(current).filter(([id, draft]) => draft !== submitted[Number(id)])));
    } catch { setFailed(true); }
  };
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel swsh-pokemon-section held-item-chance" aria-labelledby="held-item-chance-heading">
      <div className="panel-heading"><Sparkles size={18} aria-hidden="true" />
        <h2 id="held-item-chance-heading">{t('heldItemChance.title')}</h2>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <p>{t('heldItemChance.pokemonSubtitle')}</p>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('heldItemChance.title')} onStart={onStartEditSession} readOnlyReason={t('heldItemChance.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="secondary-button" type="button" disabled={!canEdit || !desired?.customRates}
          onClick={() => desired && change({ ...desired, rates: (workflow?.rates ?? []).map(String), customRates: false })}>
          <RotateCcw size={16} aria-hidden="true" /><span>{t('heldItemChance.restoreRates')}</span>
        </button>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || invalid || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDrafts({}); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'heldItemChance.draft' : 'heldItemChance.clean')}</span>
      </EditorSessionBarActions> : null}
      <div className="items-toolbar pokemon-toolbar">
        <PokemonSearchBox disabled={!workflow} value={searchText} onChange={setSearchText} />
      </div>
      <div className="items-layout swsh-pokemon-layout held-item-pokemon-layout">
        <PokemonTable records={filtered} selectedId={selected?.personalId} pendingIds={pendingIds}
          onSelect={setSelectedId} formatName={formatPokemonName} resetKey={searchText} />
        <div className="held-item-pokemon-details">
      {selected && desired ? <>
        <h3 data-localization-ignore="true">{formatPokemonName(selected)}</h3>
        <div className="held-item-slot-grid held-item-choices">
          {[0, 1, 2].map(slot => <div key={slot} className="held-item-rate-field">
            <label htmlFor={`held-item-choice-${slot}`}>{t('heldItemChance.slot', { number: slot + 1 })}</label>
            <SearchableOptionInput id={`held-item-choice-${slot}`} ariaLabel={t('heldItemChance.itemSlot', { number: slot + 1 })}
              isFiniteCatalog options={options} disabled={!canEdit} value={desired.items[slot]}
              onChange={value => { const items = [...desired.items]; items[slot] = value; change({ ...desired, items }); }} />
          </div>)}
        </div>
        <label className="held-item-custom"><input type="checkbox" checked={desired.customRates} disabled={!canEdit}
          onChange={event => change({ ...desired, customRates: event.target.checked,
            rates: event.target.checked ? desired.rates : (workflow?.rates ?? []).map(String) })} />{t('heldItemChance.customRates')}</label>
        <div className="held-item-rate-groups">
          {[0, 1].map(group => {
            const values = desired.rates.slice(group * 3, group * 3 + 3);
            const validFields = values.length === 3 && values.every(value => /^\d{1,3}$/.test(value) && Number(value) <= 100);
            const total = validFields ? values.reduce((sum, value) => sum + Number(value), 0) : null;
            return <fieldset key={group} className="held-item-rate-group">
              <legend>{t(group === 0 ? 'heldItemChance.normal' : 'heldItemChance.boosted')}</legend>
              <p>{t(group === 0 ? 'heldItemChance.normalHelp' : 'heldItemChance.boostedHelp')}</p>
              <div className="held-item-slot-grid">
                {[0, 1, 2].map(slot => <label key={slot} className="held-item-rate-field">
                  <span>{t('heldItemChance.slot', { number: slot + 1 })}</span>
                  <span className="held-item-rate-input"><input type="text" inputMode="numeric"
                    aria-label={`${t(group === 0 ? 'heldItemChance.normal' : 'heldItemChance.boosted')} ${t('heldItemChance.slot', { number: slot + 1 })}`}
                    aria-invalid={total === null || total > 100 || undefined} aria-describedby="held-item-rate-limits"
                    disabled={!canEdit} value={desired.rates[group * 3 + slot] ?? ''}
                    onChange={event => { const rates = [...desired.rates]; rates[group * 3 + slot] = event.target.value;
                      change({ ...desired, rates, customRates: true }); }} /><span aria-hidden="true">%</span></span>
                </label>)}
              </div>
              <p className="held-item-total" aria-live="polite">{total === null ? t('heldItemChance.incomplete') :
                t('heldItemChance.total', { total, remaining: Math.max(0, 100 - total) })}</p>
              {total !== null && total > 100 ? <p role="alert">{t('heldItemChance.overTotal')}</p> : null}
            </fieldset>;
          })}
        </div>
        <div className="held-item-notes">
          <p id="held-item-rate-limits">{t('heldItemChance.limits')}</p>
          <p>{t('heldItemChance.pokemonScope')}</p>
          <p>{t(desired.customRates ? 'heldItemChance.customHelp' : 'heldItemChance.equalHelp')}</p>
          <p>{t('heldItemChance.packageHelp', { editor: t('gameplaySettings.title') })}</p>
        </div>
      </> : <p>{workflow && records.length ? translateLiteral('No Pokemon selected.') : t('heldItemChance.wildMissing')}</p>}
        </div>
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
