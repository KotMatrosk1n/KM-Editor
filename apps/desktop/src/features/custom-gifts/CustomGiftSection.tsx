/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Crown, KeyRound, Plus, RefreshCw, Save, Trash2, X } from 'lucide-react';
import type { EditSession, GiftPokemonEditableField } from '../../bridge/contracts';
import { customGiftCatalogSchema, getCustomGiftPendingCatalog, maximumRoyalGiftPoolSize, parseRoyalGiftChance, royalGiftChanceWeight,
  royalGiftPoolIsValid, type CustomGiftCatalog, type CustomGiftPokemon,
  type CustomGiftWorkflow } from '../../bridge/customGiftContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import { defaultCustomGiftPokemon, updateCustomGiftField } from './customGiftFields';
import './CustomGiftSection.css';

export type CustomGiftFieldRenderProps = {
  pokemon: CustomGiftPokemon; fields: GiftPokemonEditableField[]; drafts: Record<string, string>;
  disabled: boolean; idPrefix: string; onChange: (field: string, value: string) => void;
};
type Props = {
  mode: 'mystery' | 'royal'; workflow: CustomGiftWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (catalog: CustomGiftCatalog) => Promise<boolean>; onDirtyStateChange: (dirty: boolean) => void;
  onRefresh: () => void; panelOutput: WorkflowPanelOutput;
  renderPokemonFields: (props: CustomGiftFieldRenderProps) => ReactNode;
};

export function CustomGiftSection({ mode, workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput, renderPokemonFields }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = getCustomGiftPendingCatalog(session);
  const clean = pending ?? workflow?.catalog ?? null;
  const [draft, setDraft] = useState<CustomGiftCatalog | null>(null);
  const [numericDrafts, setNumericDrafts] = useState<Record<string, Record<string, string>>>({});
  const [selection, setSelection] = useState<number | null>(null);
  const [badges, setBadges] = useState(0);
  const [failed, setFailed] = useState(false);
  const revision = useRef(0);
  const desired = draft ?? clean;
  const fields = workflow?.editableFields ?? [];
  const canEdit = isEditing && workflow?.canEdit === true && desired !== null;
  const selectedPool = desired?.royalPools.find(pool => pool.badges === badges);
  const entries = mode === 'mystery' ? desired?.codeGifts.map(gift => ({ key: gift.claimId, name: gift.name, pokemon: gift.pokemon })) ?? []
    : selectedPool?.pokemon.map((pokemon, index) => ({ key: index, name: '', pokemon })) ?? [];
  const selected = entries.find(entry => entry.key === selection) ?? entries[0];
  const recordKey = selected ? `${mode}:${mode === 'royal' ? `${badges}:` : ''}${selected.key}` : '';
  const parsed = desired ? customGiftCatalogSchema.safeParse(desired) : null;
  const invalidNumeric = Object.values(numericDrafts).some(values => Object.entries(values).some(([name, raw]) => {
    if (name === 'chance') return parseRoyalGiftChance(raw) === null;
    const field = fields.find(candidate => candidate.field === name);
    if (name === 'flawlessIvCount' && raw === '-9999') return false;
    return !/^-?\d+$/.test(raw.trim()) || !Number.isSafeInteger(Number(raw)) || !field
      || field.minimumValue !== null && Number(raw) < field.minimumValue
      || field.maximumValue !== null && Number(raw) > field.maximumValue;
  }));
  const invalid = parsed?.success === false || invalidNumeric;
  const needsRepair = workflow?.needsRepair === true && pending === null;
  const dirty = draft !== null && JSON.stringify(draft) !== JSON.stringify(clean) || invalidNumeric || isEditing && needsRepair;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.customGifts', field: 'catalog',
    message: failed ? t('customGifts.failed') : invalid && isEditing ? t('customGifts.invalid') : null });
  const edit = (change: (catalog: CustomGiftCatalog) => CustomGiftCatalog) => {
    if (!canEdit || !desired) return;
    revision.current += 1; setDraft(change(desired)); setFailed(false);
  };
  const editPokemon = (change: (pokemon: CustomGiftPokemon) => CustomGiftPokemon) => {
    if (!selected) return;
    edit(catalog => mode === 'mystery' ? { ...catalog, codeGifts: catalog.codeGifts.map(gift =>
      gift.claimId === selected.key ? { ...gift, pokemon: change(gift.pokemon) } : gift) }
      : { ...catalog, royalPools: catalog.royalPools.map(pool => pool.badges === badges
        ? { ...pool, pokemon: pool.pokemon.map((pokemon, index) => index === selected.key ? change(pokemon) : pokemon) } : pool) });
  };
  const changeNumber = (field: string, raw: string) => {
    if (!canEdit) return;
    revision.current += 1;
    setFailed(false);
    setNumericDrafts(current => {
      const values = { ...current[recordKey] };
      if (field === 'flawlessIvCount' && ['0', '3', '6'].includes(raw))
        for (const name of Object.keys(values)) if (name.startsWith('iv')) delete values[name];
      return { ...current, [recordKey]: { ...values, [field]: raw,
        ...(field === 'species' && /^\d+$/.test(raw.trim()) ? { form: '0', ability: '0', gender: '0' }
          : field === 'form' && /^\d+$/.test(raw.trim()) ? { ability: '0', gender: '0' } : {}) } };
    });
    if (/^-?\d+$/.test(raw.trim()) && Number.isSafeInteger(Number(raw))) editPokemon(pokemon => updateCustomGiftField(pokemon, field, Number(raw)));
  };
  const stage = async () => {
    if (!canEdit || !dirty || invalid || isStaging || !parsed?.success) return;
    const submitted = revision.current;
    setFailed(false);
    try {
      if (!await onStage(parsed.data)) { setFailed(true); return; }
      if (revision.current === submitted) { setDraft(null); setNumericDrafts({}); }
    } catch { setFailed(true); }
  };
  const add = () => {
    if (!desired) return;
    if (mode === 'mystery') {
      const id = desired.nextClaimId;
      if (id >= 2000 || desired.codeGifts.length >= 128) return;
      edit(catalog => ({ ...catalog, nextClaimId: id + 1, codeGifts: [...catalog.codeGifts,
        { claimId: id, name: t('customGifts.newGift'), code: `G1FT${id}`, oncePerSave: true, pokemon: defaultCustomGiftPokemon() }] }));
      setSelection(id);
    } else {
      if (entries.length >= maximumRoyalGiftPoolSize) return;
      edit(catalog => ({ ...catalog, royalPools: catalog.royalPools.map(pool => pool.badges === badges
        ? { ...pool, pokemon: [...pool.pokemon, defaultCustomGiftPokemon()], chances: [...pool.chances, entries.length === 0 ? 100 : 0] } : pool) }));
      setSelection(entries.length);
    }
  };
  const remove = () => {
    if (!selected) return;
    const removed = selected.key;
    edit(catalog => mode === 'mystery' ? { ...catalog, codeGifts: catalog.codeGifts.filter(gift => gift.claimId !== removed) }
      : { ...catalog, royalPools: catalog.royalPools.map(pool => pool.badges === badges
        ? { ...pool, pokemon: pool.pokemon.filter((_, index) => index !== removed), chances: pool.chances.filter((_, index) => index !== removed) } : pool) });
    setNumericDrafts(current => Object.fromEntries(Object.entries(current).flatMap(([key, value]) => {
      if (key === recordKey) return [];
      const prefix = `royal:${badges}:`;
      if (mode === 'royal' && key.startsWith(prefix) && Number(key.slice(prefix.length)) > removed)
        return [[`${prefix}${Number(key.slice(prefix.length)) - 1}`, value]];
      return [[key, value]];
    })));
    setSelection(null);
  };
  const selectedCode = mode === 'mystery' ? desired?.codeGifts.find(gift => gift.claimId === selected?.key) : null;
  const changeChance = (raw: string) => {
    if (!canEdit || !selected) return;
    revision.current += 1; setFailed(false);
    setNumericDrafts(current => ({ ...current, [recordKey]: { ...current[recordKey], chance: raw } }));
    const chance = parseRoyalGiftChance(raw);
    if (chance !== null) edit(catalog => ({ ...catalog, royalPools: catalog.royalPools.map(pool => pool.badges === badges
      ? { ...pool, chances: pool.chances.map((value, index) => index === selected.key ? chance : value) } : pool) }));
  };
  const poolTotal = (selectedPool?.chances.reduce((total, chance) => total + (royalGiftChanceWeight(chance) ?? 0), 0) ?? 0) / 100;
  const invalidPools = desired?.royalPools.filter(pool => !royalGiftPoolIsValid(pool)).map(pool => pool.badges) ?? [];
  const addDisabled = !canEdit || (mode === 'royal' ? entries.length >= maximumRoyalGiftPoolSize
    : (desired?.codeGifts.length ?? 0) >= 128 || (desired?.nextClaimId ?? 2000) >= 2000);
  const title = t(mode === 'royal' ? 'customGifts.royalTitle' : 'customGifts.mysteryTitle');
  const Icon = mode === 'royal' ? Crown : KeyRound;
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel custom-gifts-editor" aria-labelledby={`custom-gifts-${mode}-heading`}>
      <div className="panel-heading"><Icon size={20} aria-hidden="true" /><h2 id={`custom-gifts-${mode}-heading`}>{title}</h2>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh} disabled={isLoading || isStaging}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span></button>
      </div>
      <p>{t(mode === 'royal' ? 'customGifts.royalHelp' : 'customGifts.mysteryHelp')}</p>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={title} onStart={onStartEditSession} readOnlyReason={t('customGifts.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || invalid || isStaging} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span></button>
        <button className="danger-button" type="button" disabled={isStaging} onClick={() => onCancelEditSession(() => {
          revision.current += 1; setDraft(null); setNumericDrafts({}); setFailed(false);
        })}><X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span></button>
        <span className="draft-action-summary">{t(dirty ? 'customGifts.draft' : pending ? 'customGifts.staged' : 'customGifts.clean')}</span>
      </EditorSessionBarActions> : null}
      <label className="custom-gifts-install"><input type="checkbox" checked={desired?.mode === mode} disabled={!canEdit}
        onChange={event => edit(catalog => ({ ...catalog, mode: event.target.checked ? mode : 'none' }))} />
        <strong>{t('customGifts.install', { editor: title })}</strong></label>
      <p>{t('customGifts.exclusive')}</p>
      <p role="status">{t('customGifts.current', { editor: t(workflow?.installedMode === 'royal' ? 'customGifts.royalTitle'
        : workflow?.installedMode === 'mystery' ? 'customGifts.mysteryTitle' : 'customGifts.none') })}</p>
      {needsRepair ? <p>{t('customGifts.repair')}</p> : null}
      {mode === 'royal' ? <div className="custom-gifts-badges" role="group" aria-label={t('customGifts.badges')}>
        {Array.from({ length: 9 }, (_, count) => <button type="button" key={count} aria-pressed={count === badges}
          className={count === badges ? 'primary-button' : 'secondary-button'} onClick={() => { setBadges(count); setSelection(null); }}>
          {t('customGifts.badgeCount', { count })}</button>)}
      </div> : null}
      {mode === 'royal' ? <div className="custom-gifts-pool-summary">
        <p role="status">{t('customGifts.poolSummary', { count: entries.length, total: poolTotal })}</p>
        <p>{t('customGifts.oddsHelp')}</p>
        {invalidPools.length > 0 ? <p role="status">{t('customGifts.invalidPools', { badges: invalidPools.join(', ') })}</p> : null}
      </div> : null}
      <div className="custom-gifts-layout">
        <div className="custom-gifts-list">
          <button type="button" className="secondary-button" disabled={addDisabled} onClick={add}><Plus size={16} aria-hidden="true" />{t('customGifts.add')}</button>
          {entries.length === 0 ? <p>{t('customGifts.empty')}</p> : entries.map(entry => <button type="button" key={entry.key}
            className={selected?.key === entry.key ? 'custom-gifts-row selected' : 'custom-gifts-row'} onClick={() => setSelection(entry.key)}>
            <strong>{entry.name || fields.find(field => field.field === 'species')?.options.find(option => option.value === entry.pokemon.speciesId)?.label || entry.pokemon.speciesId}</strong>
            <span>{t('customGifts.level', { level: entry.pokemon.level })}</span>
            {mode === 'royal' ? <span>{t('customGifts.slotChance', { chance: selectedPool?.chances[entry.key] ?? 0 })}</span> : null}
          </button>)}
        </div>
        <div className="custom-gifts-detail">
          {selected ? <>
            <div className="panel-heading"><h3>{t('customGifts.pokemon')}</h3><button type="button" className="danger-button" disabled={!canEdit} onClick={remove}>
              <Trash2 size={16} aria-hidden="true" />{translateLiteral('Remove')}</button></div>
            {selectedCode ? <div className="editable-field-grid">
              <label className="path-field"><span>{t('customGifts.name')}</span><input value={selectedCode.name} disabled={!canEdit} maxLength={80}
                onChange={event => edit(catalog => ({ ...catalog, codeGifts: catalog.codeGifts.map(gift => gift.claimId === selectedCode.claimId
                  ? { ...gift, name: event.target.value } : gift) }))} /></label>
              <label className="path-field"><span>{t('customGifts.code')}</span><input value={selectedCode.code} disabled={!canEdit} maxLength={16}
                aria-describedby="custom-gifts-code-help" onChange={event => edit(catalog => ({ ...catalog, codeGifts: catalog.codeGifts.map(gift =>
                  gift.claimId === selectedCode.claimId ? { ...gift, code: event.target.value.toUpperCase() } : gift) }))} /></label>
              <label className="custom-gifts-install"><input type="checkbox" checked={selectedCode.oncePerSave} disabled={!canEdit}
                onChange={event => edit(catalog => ({ ...catalog, codeGifts: catalog.codeGifts.map(gift => gift.claimId === selectedCode.claimId
                  ? { ...gift, oncePerSave: event.target.checked } : gift) }))} />{t('customGifts.once')}</label>
              <p>{t('customGifts.identity', { id: selectedCode.claimId })}</p>
            </div> : null}
            {selectedCode ? <p id="custom-gifts-code-help">{t('customGifts.codeHelp')}</p> : null}
            {mode === 'royal' ? <label className="path-field custom-gifts-chance"><span>{t('customGifts.chance')}</span>
              <input type="text" inputMode="decimal" disabled={!canEdit}
                value={numericDrafts[recordKey]?.chance ?? String(selectedPool?.chances[selected.key] ?? 0)}
                aria-invalid={parseRoyalGiftChance(numericDrafts[recordKey]?.chance ?? String(selectedPool?.chances[selected.key] ?? 0)) === null}
                onChange={event => changeChance(event.target.value)} />
            </label> : null}
            <label className="custom-gifts-install"><input type="checkbox" checked={selected.pokemon.isEgg} disabled={!canEdit}
              onChange={event => editPokemon(pokemon => ({ ...pokemon, isEgg: event.target.checked }))} />{t('customGifts.egg')}</label>
            {renderPokemonFields({ pokemon: selected.pokemon, fields, drafts: numericDrafts[recordKey] ?? {}, disabled: !canEdit,
              idPrefix: `custom-gift-${recordKey}`, onChange: changeNumber })}
          </> : <p>{t('customGifts.select')}</p>}
        </div>
      </div>
      <p>{t('customGifts.saveHelp')}</p>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
