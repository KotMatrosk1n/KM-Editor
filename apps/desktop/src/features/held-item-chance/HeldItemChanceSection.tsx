/* SPDX-License-Identifier: GPL-3.0-only */
import { ContextHelp } from '../../components/ContextHelp';
import { RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { useEffect, useRef, useState } from 'react';
import { PokemonSearchBox, PokemonTable, filterPokemonRecords, type PokemonSelectionRecord } from '../../components/PokemonSelection';
import { RefreshCw, RotateCcw, Save, Sparkles, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { getHeldItemPendingRates, getHeldItemPokemonState, parseHeldItemRateDrafts,
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
  onStageGlobal: (rates: number[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};
type Draft = { rates: string[]; items: string[]; customRates: boolean };

export function HeldItemChanceSection({ formatPokemonName, workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onStageGlobal, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [selectedId, setSelectedId] = useState<number>();
  const [searchText, setSearchText] = useState('');
  const [drafts, setDrafts] = useState<Record<number, Draft>>({});
  const draftsRef = useRef(drafts); draftsRef.current = drafts;
  const rateVersions = useRef<Record<number, number>>({});
  const sessionGeneration = useRef(0);
  const [globalDraft, setGlobalDraft] = useState<string[] | null>(null);
  const [isGlobalStaging, setIsGlobalStaging] = useState(false);
  const globalRates = getHeldItemPendingRates(session) ?? workflow?.rates ?? [];
  const globalValues = globalDraft ?? globalRates.map(String);
  const parsedGlobal = parseHeldItemRateDrafts(globalValues);
  const globalInvalid = globalDraft !== null && parsedGlobal === null;
  const busy = isStaging || isGlobalStaging;
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
  const individualDirty = invalid || updates.length > 0;
  const dirty = individualDirty || globalDraft !== null;
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  useEffect(() => {
    sessionGeneration.current += 1;
    setDrafts({}); setGlobalDraft(null); setFailed(false); rateVersions.current = {};
  }, [session?.sessionId]);
  usePublishCommonEditorError({ domain: 'workflow.heldItemChance', field: 'rates',
    message: globalInvalid ? t('heldItemChance.invalid') : invalid ? t('heldItemChance.pokemonInvalid') : failed ? t('heldItemChance.failed') : null });
  const change = (next: Draft) => {
    if (!canEdit || !selected) return;
    if (desired?.customRates !== next.customRates || desired.rates.join(',') !== next.rates.join(','))
      rateVersions.current[selected.personalId] = (rateVersions.current[selected.personalId] ?? 0) + 1;
    setSelectedId(selected.personalId);
    setDrafts(current => ({ ...current, [selected.personalId]: next })); setFailed(false);
  };
  const stage = async () => {
    if (!canEdit || !individualDirty || busy || invalid) return;
    const submitted = draftsRef.current;
    const generation = sessionGeneration.current;
    setFailed(false);
    try {
      const accepted = await onStage(updates);
      if (generation !== sessionGeneration.current) return;
      if (!accepted) { setFailed(true); return; }
      setDrafts(current => Object.fromEntries(Object.entries(current).filter(([id, draft]) => draft !== submitted[Number(id)])));
    } catch { if (generation === sessionGeneration.current) setFailed(true); }
  };
  const stageGlobal = async () => {
    if (!canEdit || busy || parsedGlobal === null) return;
    const submitted = globalDraft;
    const versions = { ...rateVersions.current };
    const generation = sessionGeneration.current;
    setIsGlobalStaging(true); setFailed(false);
    try {
      const accepted = await onStageGlobal(parsedGlobal);
      if (generation !== sessionGeneration.current) return;
      if (!accepted) { setFailed(true); return; }
      setGlobalDraft(current => current === submitted ? null : current);
      setDrafts(current => Object.fromEntries(Object.entries(current).map(([id, draft]) => [id,
        (versions[Number(id)] ?? 0) === (rateVersions.current[Number(id)] ?? 0)
          ? { ...draft, customRates: false, rates: parsedGlobal.map(String) } : draft])));
    } catch { if (generation === sessionGeneration.current) setFailed(true); }
    finally { setIsGlobalStaging(false); }
  };
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel swsh-pokemon-section held-item-chance" aria-labelledby="held-item-chance-heading">
      <div className="panel-heading"><Sparkles size={18} aria-hidden="true" />
        <h2 id="held-item-chance-heading">{t('heldItemChance.title')}</h2>
        <ContextHelp label={t('heldItemChance.title')}>{t('heldItemChance.pokemonSubtitle')}</ContextHelp>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || busy} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('heldItemChance.title')} onStart={onStartEditSession} readOnlyReason={t('heldItemChance.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="secondary-button" type="button" disabled={!canEdit || !desired?.customRates}
          onClick={() => desired && change({ ...desired, rates: globalRates.map(String), customRates: false })}>
          <RotateCcw size={16} aria-hidden="true" /><span>{t('heldItemChance.restoreRates')}</span>
        </button>
        <button className="primary-button" type="button" disabled={!canEdit || !individualDirty || invalid || busy}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={busy}
          onClick={() => onCancelEditSession(() => { sessionGeneration.current += 1; setDrafts({}); setGlobalDraft(null); setFailed(false); rateVersions.current = {}; })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'heldItemChance.draft' : 'heldItemChance.clean')}</span>
      </EditorSessionBarActions> : null}
      <details className="held-item-global">
        <summary>{t('heldItemChance.global')}</summary>
        <p>{t('heldItemChance.globalHelp')}</p>
        <HeldItemRateGroups rates={globalValues} disabled={!canEdit} global
          onChange={rates => { setGlobalDraft(rates); setFailed(false); }} />
        <p id="held-item-global-limits">{t('heldItemChance.limits')}</p>
        <p>{t('heldItemChance.equalHelp')}</p>
        <button className="primary-button" type="button" disabled={!canEdit || busy || parsedGlobal === null}
          aria-busy={isGlobalStaging || undefined} onClick={() => void stageGlobal()}>
          <Save size={16} aria-hidden="true" /><span>{t('heldItemChance.stageGlobal')}</span>
        </button>
      </details>
      <div className="items-toolbar pokemon-toolbar">
        <PokemonSearchBox disabled={!workflow} value={searchText} onChange={setSearchText} />
      </div>
      <RecordEditorWorkspace id="heldItemChance" className="items-layout swsh-pokemon-layout held-item-pokemon-layout" browser={
        <PokemonTable records={filtered} selectedId={selected?.personalId} pendingIds={pendingIds}
          onSelect={setSelectedId} formatName={formatPokemonName} resetKey={searchText} />
      }>
        <div className="held-item-pokemon-details">
      {selected && desired ? <>
        <div className="context-help-heading held-item-record-heading">
          <h3 data-localization-ignore="true">{formatPokemonName(selected)}</h3>
          <ContextHelp label={t('heldItemChance.title')}>
            <p>{t('heldItemChance.pokemonScope')}</p>
            <p>{t(desired.customRates ? 'heldItemChance.customHelp' : 'heldItemChance.equalHelp')}</p>
            <p>{t('heldItemChance.packageHelp', { editor: t('gameplaySettings.title') })}</p>
          </ContextHelp>
        </div>
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
            rates: event.target.checked ? desired.rates : globalRates.map(String) })} />{t('heldItemChance.customRates')}</label>
        <HeldItemRateGroups rates={desired.rates} disabled={!canEdit}
          onChange={rates => change({ ...desired, rates, customRates: true })} />
        <p className="field-hint" id="held-item-rate-limits">{t('heldItemChance.limits')}</p>
      </> : <p>{workflow && records.length ? translateLiteral('No Pokemon selected.') : t('heldItemChance.wildMissing')}</p>}
        </div>
      </RecordEditorWorkspace>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}

function HeldItemRateGroups({ rates, disabled, global = false, onChange }: {
  rates: string[]; disabled: boolean; global?: boolean; onChange: (rates: string[]) => void;
}) {
  const { t: translate } = useLocalization();
  return <div className="held-item-rate-groups">
    {[0, 1].map(group => {
      const label = translate(group === 0 ? 'heldItemChance.normal' : 'heldItemChance.boosted');
      const values = rates.slice(group * 3, group * 3 + 3);
      const validFields = values.length === 3 && values.every(value => /^\d{1,3}$/.test(value) && Number(value) <= 100);
      const total = validFields ? values.reduce((sum, value) => sum + Number(value), 0) : null;
      return <fieldset key={group} className="held-item-rate-group">
        <legend>{label}</legend>
        <p>{translate(group === 0 ? 'heldItemChance.normalHelp' : 'heldItemChance.boostedHelp')}</p>
        <div className="held-item-slot-grid">
          {[0, 1, 2].map(slot => <label key={slot} className="held-item-rate-field">
            <span>{translate('heldItemChance.slot', { number: slot + 1 })}</span>
            <span className="held-item-rate-input"><input type="text" inputMode="numeric"
              aria-label={`${global ? `${translate('heldItemChance.global')} ` : ''}${label} ${translate('heldItemChance.slot', { number: slot + 1 })}`}
              aria-invalid={total === null || total > 100 || undefined}
              aria-describedby={global ? 'held-item-global-limits' : 'held-item-rate-limits'}
              disabled={disabled} value={rates[group * 3 + slot] ?? ''}
              onChange={event => { const next = [...rates]; next[group * 3 + slot] = event.target.value; onChange(next); }} />
              <span aria-hidden="true">%</span></span>
          </label>)}
        </div>
        <p className="held-item-total" aria-live="polite">{total === null ? translate('heldItemChance.incomplete') :
          translate('heldItemChance.total', { total, remaining: Math.max(0, 100 - total) })}</p>
        {total !== null && total > 100 ? <p role="alert">{translate('heldItemChance.overTotal')}</p> : null}
      </fieldset>;
    })}
  </div>;
}
